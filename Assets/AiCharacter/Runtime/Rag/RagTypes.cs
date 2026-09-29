using System;
using System.Collections.Generic;
using System.IO;

namespace DynamicNpcs
{
    [Serializable]
    public class RagChunk
    {
        public string text;
        public float[] embedding;
    }

    /// <summary>Binary index format: compact, avoids Unity's slow JSON/YAML serialization for large float arrays.</summary>
    public static class RagIndexIO
    {
        private const int Magic = 0x52414731; // "RAG1"

        public static void Save(string path, List<RagChunk> chunks)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);

            w.Write(Magic);
            w.Write(chunks.Count);
            foreach (var c in chunks)
            {
                w.Write(c.text);
                w.Write(c.embedding.Length);
                foreach (float f in c.embedding)
                    w.Write(f);
            }
        }

        public static RagChunk[] Load(string path)
        {
            using var fs = File.OpenRead(path);
            using var r = new BinaryReader(fs);

            int magic = r.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException($"'{path}' is not a valid RAG index file.");

            int count = r.ReadInt32();
            var chunks = new RagChunk[count];
            for (int i = 0; i < count; i++)
            {
                string text = r.ReadString();
                int dim = r.ReadInt32();
                var embedding = new float[dim];
                for (int j = 0; j < dim; j++)
                    embedding[j] = r.ReadSingle();
                chunks[i] = new RagChunk { text = text, embedding = embedding };
            }
            return chunks;
        }
    }
}