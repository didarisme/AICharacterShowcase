using UnityEngine;

namespace DynamicNpcs
{
    /// <summary>
    /// A knowledge source for RAG: a text file plus chunking settings. Bake it
    /// (Editor > Dynamic NPCs > RAG tooling) to produce the binary index NpcPersona references.
    /// </summary>
    [CreateAssetMenu(fileName = "RagSource", menuName = "Dynamic NPCs/RAG Source", order = 4)]
    public class RagSourceAsset : ScriptableObject
    {
        [Tooltip("Plain text source, e.g. a lore book exported as .txt.")]
        public TextAsset sourceFile;

        [Min(200)]
        [Tooltip("Target characters per chunk. ~600-1000 works well for prose.")]
        public int chunkChars = 800;

        [Min(0)]
        [Tooltip("Characters repeated at the start of each chunk from the end of the previous one, so answers near a chunk boundary aren't lost.")]
        public int overlapChars = 120;

        [Tooltip("Override the global Rag Top K for this specific source (0 = use Settings' default). Useful for small, high-precision sources like a single fact - set to 1.")]
        [Min(0)] public int topKOverride = 0;

        /// <summary>Where the baked binary index for this source lives.</summary>
        public string IndexPath => $"Assets/StreamingAssets/DynamicNPCs/rag/{name}.bin";
    }
}