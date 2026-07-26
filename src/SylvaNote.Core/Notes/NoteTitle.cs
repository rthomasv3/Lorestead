using System.Text;

namespace SylvaNote.Core.Notes
{
    public static class NoteTitle
    {
        // Titles stay free-form: a colon or a slash is legitimate content, and the
        // export keeps the true title in front matter while sanitizing only the
        // filename (features/export.md). What gets removed here is what is never
        // content - control characters, and the newlines and runs of whitespace that
        // would break a single-line tree row and a YAML scalar.
        public static string Normalize(string title)
        {
            string result = string.Empty;

            if (!string.IsNullOrEmpty(title))
            {
                StringBuilder builder = new StringBuilder(title.Length);
                bool pendingSpace = false;

                foreach (char character in title)
                {
                    if (char.IsWhiteSpace(character) || char.IsControl(character))
                    {
                        pendingSpace = builder.Length > 0;
                    }
                    else
                    {
                        if (pendingSpace)
                        {
                            builder.Append(' ');
                            pendingSpace = false;
                        }
                        builder.Append(character);
                    }
                }

                result = builder.ToString();
            }

            return result;
        }
    }
}
