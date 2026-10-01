using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DynamicNpcs
{
    /// <summary>
    /// A simple BM25 lexical (keyword) search index over a set of chunks. Complements
    /// semantic (embedding) search: BM25 finds exact/near-exact term matches (names,
    /// numbers, rare words) that an embedding's averaged vector can under-weight,
    /// while semantic search handles paraphrased/synonymous questions BM25 can't.
    /// Built in-memory from already-loaded RagChunk text - no HTTP calls, no baking
    /// step needed, negligible cost next to the embedding request per query.
    /// </summary>
    public class RagBm25Index
    {
        private const float K1 = 1.5f;
        private const float B = 0.75f;

        private static readonly Regex TokenRegex = new Regex(@"[A-Za-z0-9']+", RegexOptions.Compiled);

        private readonly string[][] _docTerms;      // per-chunk tokenized text
        private readonly int[] _docLength;
        private readonly float _avgDocLength;
        private readonly Dictionary<string, List<int>> _postings; // term -> chunk indices containing it
        private readonly Dictionary<string, int> _docFreq;        // term -> number of chunks containing it
        private readonly int _docCount;

        private RagBm25Index(string[][] docTerms, Dictionary<string, List<int>> postings, Dictionary<string, int> docFreq)
        {
            _docTerms = docTerms;
            _docCount = docTerms.Length;
            _docLength = docTerms.Select(t => t.Length).ToArray();
            _avgDocLength = _docLength.Length > 0 ? (float)_docLength.Average() : 0f;
            _postings = postings;
            _docFreq = docFreq;
        }

        public static RagBm25Index Build(RagChunk[] chunks)
        {
            var docTerms = new string[chunks.Length][];
            var postings = new Dictionary<string, List<int>>();
            var docFreq = new Dictionary<string, int>();

            for (int i = 0; i < chunks.Length; i++)
            {
                var terms = Tokenize(chunks[i].text);
                docTerms[i] = terms;

                foreach (string term in terms.Distinct())
                {
                    if (!postings.TryGetValue(term, out var list))
                        postings[term] = list = new List<int>();
                        
                    list.Add(i);
                    docFreq[term] = docFreq.GetValueOrDefault(term, 0) + 1;
                }
            }

            return new RagBm25Index(docTerms, postings, docFreq);
        }

        /// <summary>Returns (chunkIndex, bm25Score) for the top-N matching chunks, best first. Chunks with zero term overlap are omitted.</summary>
        public List<(int index, float score)> TopN(string query, int n)
        {
            var queryTerms = Tokenize(query);
            if (queryTerms.Length == 0 || _docCount == 0)
                return new List<(int, float)>();

            var scores = new Dictionary<int, float>();

            foreach (string term in queryTerms.Distinct())
            {
                if (!_postings.TryGetValue(term, out var docs))
                    continue;

                int nt = _docFreq[term];
                // Standard BM25 idf, floored at a small positive value so common terms
                // still contribute a little rather than going negative.
                float idf = MathF.Max(0.01f,
                    MathF.Log((_docCount - nt + 0.5f) / (nt + 0.5f) + 1f));

                foreach (int docIndex in docs)
                {
                    int freq = CountTerm(_docTerms[docIndex], term);
                    float dl = _docLength[docIndex];
                    float denom = freq + K1 * (1 - B + B * dl / (_avgDocLength + 1e-6f));
                    float termScore = idf * (freq * (K1 + 1)) / (denom + 1e-6f);

                    scores[docIndex] = scores.GetValueOrDefault(docIndex, 0f) + termScore;
                }
            }

            return scores
                .OrderByDescending(kv => kv.Value)
                .Take(n)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        private static int CountTerm(string[] terms, string term)
        {
            int count = 0;

            foreach (string t in terms)
                if (t == term) count++;
                
            return count;
        }

        private static string[] Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            var matches = TokenRegex.Matches(text);
            var result = new string[matches.Count];

            for (int i = 0; i < matches.Count; i++)
                result[i] = matches[i].Value.ToLowerInvariant();

            return result;
        }
    }
}
