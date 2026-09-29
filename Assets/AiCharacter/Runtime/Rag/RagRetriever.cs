using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DynamicNpcs
{
    /// <summary>Loads a baked RAG index and finds the top-K most relevant chunks for a query.</summary>
    public static class RagRetriever
    {
        private static readonly Dictionary<string, RagChunk[]> IndexCache = new Dictionary<string, RagChunk[]>();

        public static async Task<string> RetrieveContextAsync(
            DynamicNpcSettings settings, RagSourceAsset[] sources, string query, CancellationToken ct)
        {
            if (sources == null || sources.Length == 0)
                return "";

            await EmbeddedEmbeddingServer.EnsureRunningAsync(settings, ct);
            float[] queryEmbedding = await RagEmbeddingClient.GetEmbeddingAsync(settings, query, ct);

            var results = new List<string>();
            foreach (var source in sources)
            {
                if (source == null) continue;
                var chunks = LoadIndex(source.IndexPath);
                if (chunks == null || chunks.Length == 0) continue;

                // Per-source top-K, so a small source's fact isn't drowned out by a
                // large source's many similarly-scored chunks in a global ranking.
                int k = source.topKOverride > 0 ? source.topKOverride : settings.ragTopK;

                var top = chunks
                    .Select(c => (chunk: c, score: CosineSimilarity(queryEmbedding, c.embedding)))
                    .OrderByDescending(x => x.score)
                    .Take(k);

                foreach (var (chunk, score) in top)
                    Debug.Log($"[RAG] score={score:0.000} chunk=\"{chunk.text.Substring(0, Math.Min(60, chunk.text.Length))}...\"");

                var filtered = top.Where(x => x.score >= settings.ragMinScore).Select(x => x.chunk.text);
                results.AddRange(filtered);
            }

            return string.Join("\n\n", results);
        }

        private static RagChunk[] LoadIndex(string path)
        {
            if (IndexCache.TryGetValue(path, out var cached))
                return cached;

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[DynamicNPCs] RAG index not found at '{path}'. Bake it first.");
                return null;
            }

            var chunks = RagIndexIO.Load(path);
            IndexCache[path] = chunks;
            
            return chunks;
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