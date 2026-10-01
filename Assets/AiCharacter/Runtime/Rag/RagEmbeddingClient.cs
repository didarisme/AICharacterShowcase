using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace DynamicNpcs
{
    /// <summary>
    /// Single place that talks to the embedding server's /embedding endpoint and
    /// parses its response. llama-server can return either a flat vector
    /// ("embedding": [0.1, 0.2, ...]) or a nested one
    /// ("embedding": [[0.1, 0.2, ...]] - one row per token before pooling, or a
    /// single pooled row wrapped in an extra array) depending on version/model.
    /// JsonUtility cannot deserialize jagged arrays (float[][]) at all - it silently
    /// produces a garbage length-1 array instead of throwing, which is why this is
    /// parsed by hand rather than left to JsonUtility. RagBaker and RagRetriever both
    /// go through this so the two can never drift out of sync again.
    /// </summary>
    public static class RagEmbeddingClient
    {
        public static async Task<float[]> GetEmbeddingAsync(
            DynamicNpcSettings settings, string text, CancellationToken ct)
        {
            string url = settings.EmbeddedEmbeddingRootUrl + "/embedding";
            string json = "{\"content\":\"" + JsonText.Escape(text) + "\"}";

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = settings.ttsTimeoutSeconds;

            using (ct.Register(req.Abort))
            {
                await req.SendWebRequest();
                ct.ThrowIfCancellationRequested();

                if (req.result != UnityWebRequest.Result.Success)
                    throw new Exception($"Embedding request failed ({url}): {req.error} - {req.downloadHandler.text}");

                return ParseEmbedding(req.downloadHandler.text);
            }
        }

        /// <summary>
        /// Hand-rolled parse of llama-server's /embedding response: finds the
        /// "embedding" field's array and reads whichever level actually holds
        /// numbers - handles both [ ... ] and [ [ ... ] ] without relying on
        /// JsonUtility's broken jagged-array support.
        /// </summary>
        private static float[] ParseEmbedding(string responseJson)
        {
            int embeddingKeyIdx = responseJson.IndexOf("\"embedding\"", StringComparison.Ordinal);
            
            if (embeddingKeyIdx < 0)
                throw new Exception("No 'embedding' field in response: " + Truncate(responseJson));

            int colonIdx = responseJson.IndexOf(':', embeddingKeyIdx);
            int firstBracket = responseJson.IndexOf('[', colonIdx);

            if (firstBracket < 0)
                throw new Exception("Malformed 'embedding' field: " + Truncate(responseJson));

            // Is the next non-whitespace char after the opening bracket another
            // bracket? If so this is the nested form [[ ... ]]; step in one level.
            int innerStart = firstBracket + 1;

            while (innerStart < responseJson.Length && char.IsWhiteSpace(responseJson[innerStart]))
                innerStart++;

            int arrayStart, arrayEnd;

            if (innerStart < responseJson.Length && responseJson[innerStart] == '[')
            {
                arrayStart = innerStart + 1;
                arrayEnd = responseJson.IndexOf(']', arrayStart);
            }
            else
            {
                arrayStart = firstBracket + 1;
                arrayEnd = responseJson.IndexOf(']', arrayStart);
            }

            if (arrayEnd < 0)
                throw new Exception("Could not find end of embedding array: " + Truncate(responseJson));

            string numbersCsv = responseJson.Substring(arrayStart, arrayEnd - arrayStart);
            string[] parts = numbersCsv.Split(',');
            var result = new float[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(
                        parts[i].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out result[i]))
                    throw new Exception($"Could not parse embedding value '{parts[i]}' in: {Truncate(responseJson)}");
            }

            return result;
        }

        private static string Truncate(string s) => s.Length > 200 ? s.Substring(0, 200) + "..." : s;
    }
}
