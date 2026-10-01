using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DynamicNpcs
{
    /// <summary>
    /// Loads baked RAG indexes and finds the top-K most relevant chunks for a query,
    /// per source. Combines semantic (embedding cosine similarity) and BM25 (lexical/
    /// keyword) rankings via Reciprocal Rank Fusion: semantic search handles
    /// paraphrased questions, BM25 catches exact names/numbers/rare terms that a
    /// chunk's averaged embedding can under-represent. Fusing ranks rather than raw
    /// scores avoids having to make cosine similarity and BM25 scores - which live on
    /// unrelated scales - comparable.
    /// </summary>
    public static class RagRetriever
    {
        // Standard RRF damping constant (see Cormack et al., "Reciprocal Rank Fusion").
        // Larger = flatter weighting of lower ranks; 60 is the commonly used default.
        private const int RrfK = 60;

        // How many candidates each individual ranking (semantic, BM25) contributes
        // before fusion. Wider than the final top-K so a chunk that's e.g. #1 in BM25
        // but outside semantic's immediate top few still has a chance to be pulled in.
        private const int CandidatePoolSize = 20;

        private static readonly Dictionary<string, RagChunk[]> ChunkCache = new Dictionary<string, RagChunk[]>();
        private static readonly Dictionary<string, RagBm25Index> Bm25Cache = new Dictionary<string, RagBm25Index>();

        public static async Task<string> RetrieveContextAsync(
            DynamicNpcSettings settings, RagSourceAsset[] sources, string query, CancellationToken ct)
        {
            if (sources == null || sources.Length == 0)
                return "";

            float[] queryEmbedding = null;
            if (settings.useRag)
            {
                await EmbeddedEmbeddingServer.EnsureRunningAsync(settings, ct);
                queryEmbedding = await RagEmbeddingClient.GetEmbeddingAsync(settings, query, ct);
            }

            var results = new List<string>();
            foreach (var source in sources)
            {
                if (source == null) continue;
                var chunks = LoadChunks(source.IndexPath);
                if (chunks == null || chunks.Length == 0) continue;

                int k = source.topKOverride > 0 ? source.topKOverride : settings.ragTopK;
                var fused = FuseRankings(settings, source, chunks, queryEmbedding, query);

                var top = fused
                    .Take(k)
                    .Where(x => x.semanticScore < 0 || x.semanticScore >= settings.ragMinScore)
                    .ToList();

// #if UNITY_EDITOR || DEVELOPMENT_BUILD
//                 foreach (var x in top)
//                     Debug.Log($"[RAG] source={source.name} rrf={x.rrfScore:0.0000} semScore={(x.semanticScore < 0 ? "n/a (BM25-only)" : x.semanticScore.ToString("0.000"))} chunk=\"{chunks[x.index].text.Substring(0, Math.Min(60, chunks[x.index].text.Length))}...\"");
// #endif

                results.AddRange(top.Select(x => chunks[x.index].text));
            }

            return string.Join("\n\n", results);
        }

        private struct FusedResult
        {
            public int index;
            public float rrfScore;
            public float semanticScore; // -1 if semantic search was skipped for this candidate
        }

        private static List<FusedResult> FuseRankings(
            DynamicNpcSettings settings, RagSourceAsset source, RagChunk[] chunks,
            float[] queryEmbedding, string query)
        {
            // rank-position (0 = best) per chunk index, per ranking method
            var semanticRank = new Dictionary<int, int>();
            var semanticScores = new Dictionary<int, float>();

            if (queryEmbedding != null)
            {
                var scored = Enumerable.Range(0, chunks.Length)
                    .Select(i => (index: i, score: CosineSimilarity(queryEmbedding, chunks[i].embedding)))
                    .OrderByDescending(x => x.score)
                    .Take(CandidatePoolSize)
                    .ToList();
                for (int r = 0; r < scored.Count; r++)
                {
                    semanticRank[scored[r].index] = r;
                    semanticScores[scored[r].index] = scored[r].score;
                }
            }

            var bm25Rank = new Dictionary<int, int>();

            if (settings.useKeywordSearch)
            {
                var bm25 = GetOrBuildBm25(source.IndexPath, chunks);
                var bm25Top = bm25.TopN(query, CandidatePoolSize);
                for (int r = 0; r < bm25Top.Count; r++)
                    bm25Rank[bm25Top[r].index] = r;
            }

            var candidateIndices = new HashSet<int>(semanticRank.Keys);
            candidateIndices.UnionWith(bm25Rank.Keys);

            var fused = new List<FusedResult>(candidateIndices.Count);
            foreach (int idx in candidateIndices)
            {
                float rrf = 0f;

                if (semanticRank.TryGetValue(idx, out int sr))
                    rrf += 1f / (RrfK + sr + 1);
                if (bm25Rank.TryGetValue(idx, out int br))
                    rrf += 1f / (RrfK + br + 1);

                fused.Add(new FusedResult
                {
                    index = idx,
                    rrfScore = rrf,
                    // Only enforce the semantic min-score filter for chunks that were
                    // actually ranked by semantic search; a BM25-only hit (exact term
                    // match with no embedding candidacy) shouldn't be filtered by it.
                    semanticScore = semanticScores.TryGetValue(idx, out float ss) ? ss : -1f,
                });
            }

            return fused.OrderByDescending(x => x.rrfScore).ToList();
        }

        private static RagChunk[] LoadChunks(string path)
        {
            if (ChunkCache.TryGetValue(path, out var cached))
                return cached;

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[DynamicNPCs] RAG index not found at '{path}'. Bake it first.");
                return null;
            }

            var chunks = RagIndexIO.Load(path);
            ChunkCache[path] = chunks;
            return chunks;
        }

        private static RagBm25Index GetOrBuildBm25(string path, RagChunk[] chunks)
        {
            if (Bm25Cache.TryGetValue(path, out var cached))
                return cached;
            var index = RagBm25Index.Build(chunks);
            Bm25Cache[path] = index;
            return index;
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0, magA = 0, magB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }
            
            return dot / (Mathf.Sqrt(magA) * Mathf.Sqrt(magB) + 1e-8f);
        }
    }
}
