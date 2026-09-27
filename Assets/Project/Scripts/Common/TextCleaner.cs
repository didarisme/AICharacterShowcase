using System.Text.RegularExpressions;

namespace DynamicNpcs
{
    /// <summary>
    /// Cleans an LLM reply before it is shown and sent to TTS. The system prompt asks for
    /// plain text, but small models still slip in *emphasis*, *actions* and emojis.
    /// </summary>
    public static class TextCleaner
    {
        // *word*, **word**, _word_, __word__ (same marker on both sides).
        private static readonly Regex Wrapped = new Regex(@"(\*{1,3}|_{1,3})(\S(?:.*?\S)?)\1", RegexOptions.Compiled);
        // Stray markdown characters left after unwrapping.
        private static readonly Regex Leftover = new Regex(@"[*_#`~|]", RegexOptions.Compiled);
        // Most emojis are surrogate pairs; FE0F/200D are emoji variation selector and joiner.
        private static readonly Regex Emoji = new Regex(@"[\uD800-\uDFFF\uFE0F\u200D]", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s{2,}", RegexOptions.Compiled);

        /// <param name="dropActions">
        /// Multi-word wrapped spans (*smiles warmly*) are treated as stage directions and
        /// removed; single words (*really*) are treated as emphasis and kept without markers.
        /// </param>
        public static string Clean(string text, bool dropActions = true)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            text = Wrapped.Replace(text, m =>
            {
                string inner = m.Groups[2].Value;
                return dropActions && inner.Contains(" ") ? "" : inner;
            });

            text = Leftover.Replace(text, "");
            text = Emoji.Replace(text, "");

            return Spaces.Replace(text, " ").Trim();
        }
    }
}
