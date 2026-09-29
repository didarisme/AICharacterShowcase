using System.Threading;
using System.Threading.Tasks;

namespace DynamicNpcs
{
    /// <summary>The embedded llama-server that serves text embeddings for RAG.</summary>
    public static class EmbeddedEmbeddingServer
    {
        private static readonly LlamaServerHost Host = new LlamaServerHost("embedding");

        public static string LogText => Host.LogText;
        public static bool IsRunning => Host.IsRunning;

        public static Task EnsureRunningAsync(DynamicNpcSettings settings, CancellationToken cancellationToken)
            => Host.EnsureRunningAsync(ConfigFrom(settings), cancellationToken);

        public static void Shutdown() => Host.Shutdown();

        private static LlamaServerConfig ConfigFrom(DynamicNpcSettings s) => new LlamaServerConfig
        {
            exePath = string.IsNullOrEmpty(s.embeddingServerPath) ? s.llamaServerPath : s.embeddingServerPath,
            modelPath = s.embeddingModelPath,
            port = s.embeddingPort,
            gpuLayers = s.embeddingGpuLayers,
            contextSize = s.embeddingContextSize,
            // --embedding puts llama-server in embedding-output mode; required for /embedding to work.
            extraArgs = string.IsNullOrEmpty(s.embeddingExtraServerArgs)
                ? "--embedding"
                : s.embeddingExtraServerArgs + " --embedding",
            startupTimeoutSeconds = s.embeddedStartupTimeoutSeconds,
        };
    }
}