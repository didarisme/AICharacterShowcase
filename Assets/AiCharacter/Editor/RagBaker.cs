using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace DynamicNpcs.Editor
{
    /// <summary>
    /// Chunks a RagSourceAsset's text, embeds each chunk via the embedded embedding
    /// server, and saves the result as a binary index next to the source asset.
    /// </summary>
    public static class RagBaker
    {
        private const int BatchSize = 20;

        public static async Task BakeAsync(RagSourceAsset source, DynamicNpcSettings settings, Action<float, string> onProgress,
            CancellationToken ct)
        {
            if (source.sourceFile == null)
                throw new Exception($"RagSource '{source.name}' has no source file assigned.");

            await EmbeddedEmbeddingServer.EnsureRunningAsync(settings, ct);

            var rawChunks = ChunkTextWithOverlap(source.sourceFile.text, source.chunkChars, source.overlapChars);
            if (rawChunks.Count == 0)
                throw new Exception($"RagSource '{source.name}' produced no chunks - is the source file empty?");

            var chunks = new List<RagChunk>(rawChunks.Count);

            for (int i = 0; i < rawChunks.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = rawChunks.Skip(i).Take(BatchSize).ToList();
                var tasks = batch.Select(text => RagEmbeddingClient.GetEmbeddingAsync(settings, text, ct)).ToArray();
                var embeddings = await Task.WhenAll(tasks);

                for (int j = 0; j < embeddings.Length; j++)
                    chunks.Add(new RagChunk { text = batch[j], embedding = embeddings[j] });

                int done = Math.Min(i + BatchSize, rawChunks.Count);
                onProgress?.Invoke((float)done / rawChunks.Count, $"Embedded {done}/{rawChunks.Count} chunks");
            }

            RagIndexIO.Save(source.IndexPath, chunks);
            AssetDatabase.Refresh();
        }

        /// <summary>Splits on paragraph/sentence boundaries where possible, with a repeated tail for context continuity.</summary>
        private static List<string> ChunkTextWithOverlap(string text, int chunkChars, int overlapChars)
        {
            var result = new List<string>();
            int pos = 0;
            while (pos < text.Length)
            {
                int len = Math.Min(chunkChars, text.Length - pos);
                int end = pos + len;

                if (end < text.Length)
                {
                    int lastBreak = text.LastIndexOfAny(new[] { '\n', '.', ' ' }, end - 1, len);
                    if (lastBreak > pos)
                        end = lastBreak + 1;
                }

                string chunk = text.Substring(pos, end - pos).Trim();
                if (chunk.Length > 0)
                    result.Add(chunk);

                if (end >= text.Length)
                    break;

                pos = Math.Max(pos + 1, end - overlapChars);
            }
            return result;
        }
    }
}