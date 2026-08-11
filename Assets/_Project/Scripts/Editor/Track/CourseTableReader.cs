using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Reads the original's generated course tables out of <c>kart_course_data.c</c>.
    ///
    /// That file is machine-written by the original's own asset pipeline, which
    /// decodes each <c>track.1s</c>, so it is the checkpoint data itself rather
    /// than a description of it. Reading the C directly is deliberate: the
    /// alternative is transcribing thirty-seven thousand floats by hand, and a
    /// transcription is a copy that can drift.
    ///
    /// Only the five shapes that file actually emits are understood, and anything
    /// else is an error rather than a guess:
    ///
    /// <code>
    /// static const KartCourseRecord  SYM[] = { {{x,y,z},{x,y,z}}, ... };
    /// static const KartCourseElement SYM[] = { {name, {{..},{..}}, extra, records, count}, ... };
    /// static const KartCourseSection SYM[] = { {elements, count, start, end, final, reverse,
    ///                                           alternatives, counts, count}, ... };
    /// static const KartCourseSection *const SYM[] = {SYM, SYM};
    /// static const KartCourseAsset COURSES[] = { {"track", SYM, count}, ... };
    /// </code>
    /// </summary>
    internal static class CourseTableReader
    {
        internal sealed class Element
        {
            public string Name;
            public string Extra;

            /// <summary>The gate quad: two triangles of three points, eighteen floats.</summary>
            public float[] Corners;

            /// <summary>Centreline records, six floats each: position then direction.</summary>
            public float[] Records;

            public int RecordCount => Records.Length / 6;
        }

        internal sealed class Section
        {
            /// <summary>Null for a branch, which has no element run of its own.</summary>
            public Element[] Elements;

            public string Start;
            public string End;
            public string Final;
            public bool Reverse;
            public Section[][] Alternatives;
        }

        internal sealed class Course
        {
            public string Track;
            public Section[] Sections;
        }

        /// <summary>The courses, in the order <c>COURSES[]</c> lists them.</summary>
        internal static List<Course> Read(string source)
        {
            var records = new Dictionary<string, float[]>();
            var elements = new Dictionary<string, Element[]>();
            var sections = new Dictionary<string, Section[]>();
            var groups = new Dictionary<string, string[]>();

            // Declaration order is dependency order in a C file, so one pass in
            // file order always has what the next declaration refers to.
            foreach (Declaration declaration in Declarations(source))
            {
                switch (declaration.Type)
                {
                    case "KartCourseRecord":
                        records[declaration.Symbol] = ReadFloats(declaration.Body);
                        break;
                    case "KartCourseElement":
                        elements[declaration.Symbol] = ReadElements(declaration, records);
                        break;
                    case "KartCourseSection":
                        if (declaration.IsPointerArray)
                        {
                            groups[declaration.Symbol] = SplitTopLevel(declaration.Body);
                        }
                        else
                        {
                            sections[declaration.Symbol] = ReadSections(declaration, elements, sections, groups);
                        }
                        break;
                    case "KartCourseAsset":
                        return ReadCourses(declaration, sections);
                }
            }

            throw new FormatException("kart_course_data.c has no COURSES[] table.");
        }

        // ------------------------------------------------------- declarations

        private readonly struct Declaration
        {
            public Declaration(string type, string symbol, bool isPointerArray, string body)
            {
                Type = type;
                Symbol = symbol;
                IsPointerArray = isPointerArray;
                Body = body;
            }

            public string Type { get; }
            public string Symbol { get; }
            public bool IsPointerArray { get; }

            /// <summary>What is between the initialiser's outermost braces.</summary>
            public string Body { get; }
        }

        private static readonly Regex DeclarationPattern = new Regex(
            @"static\s+const\s+(?<type>Kart\w+)\s+(?<pointer>\*\s*const\s+)?(?<symbol>\w+)\s*\[\s*\]\s*=\s*\{",
            RegexOptions.Compiled);

        private static IEnumerable<Declaration> Declarations(string source)
        {
            for (Match match = DeclarationPattern.Match(source); match.Success;)
            {
                int open = match.Index + match.Length - 1;
                int close = MatchingBrace(source, open);
                yield return new Declaration(
                    match.Groups["type"].Value,
                    match.Groups["symbol"].Value,
                    match.Groups["pointer"].Success,
                    source.Substring(open + 1, close - open - 1));

                match = DeclarationPattern.Match(source, close);
            }
        }

        private static int MatchingBrace(string source, int open)
        {
            int depth = 0;
            for (int index = open; index < source.Length; ++index)
            {
                char c = source[index];
                if (c == '"')
                {
                    index = EndOfString(source, index);
                    continue;
                }
                if (c == '{') ++depth;
                else if (c == '}' && --depth == 0) return index;
            }
            throw new FormatException($"Unbalanced braces from offset {open}.");
        }

        private static int EndOfString(string source, int quote)
        {
            for (int index = quote + 1; index < source.Length; ++index)
            {
                if (source[index] == '\\') { ++index; continue; }
                if (source[index] == '"') return index;
            }
            throw new FormatException($"Unterminated string at offset {quote}.");
        }

        // ------------------------------------------------------------ splitting

        /// <summary>
        /// The comma-separated items of one initialiser body, with commas inside
        /// nested braces, parentheses and strings left alone.
        /// </summary>
        private static string[] SplitTopLevel(string body)
        {
            var items = new List<string>();
            int depth = 0;
            int start = 0;

            for (int index = 0; index < body.Length; ++index)
            {
                char c = body[index];
                if (c == '"') { index = EndOfString(body, index); continue; }
                if (c == '{' || c == '(') ++depth;
                else if (c == '}' || c == ')') --depth;
                else if (c == ',' && depth == 0)
                {
                    items.Add(body.Substring(start, index - start).Trim());
                    start = index + 1;
                }
            }

            string tail = body.Substring(start).Trim();
            if (tail.Length != 0) items.Add(tail);
            return items.ToArray();
        }

        /// <summary>The items of a body whose entries are themselves brace groups.</summary>
        private static string[] SplitEntries(string body)
        {
            var entries = new List<string>();
            foreach (string item in SplitTopLevel(body))
            {
                if (item.Length == 0) continue;
                if (item[0] != '{') throw new FormatException($"Expected a brace group, found {item}.");
                entries.Add(item.Substring(1, item.Length - 2));
            }
            return entries.ToArray();
        }

        // -------------------------------------------------------------- values

        private static readonly Regex FloatPattern = new Regex(
            @"-?\d+(\.\d+)?([eE][+-]?\d+)?f", RegexOptions.Compiled);

        private static float[] ReadFloats(string body)
        {
            MatchCollection matches = FloatPattern.Matches(body);
            var values = new float[matches.Count];
            for (int index = 0; index < matches.Count; ++index)
            {
                string text = matches[index].Value;
                values[index] = float.Parse(
                    text.Substring(0, text.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture);
            }
            return values;
        }

        /// <summary>A C string literal or NULL.</summary>
        private static string ReadString(string text)
        {
            text = text.Trim();
            if (text == "NULL") return null;
            if (text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"')
            {
                throw new FormatException($"Expected a string or NULL, found {text}.");
            }
            return text.Substring(1, text.Length - 2);
        }

        private static T Lookup<T>(Dictionary<string, T> table, string symbol)
        {
            if (!table.TryGetValue(symbol, out T value))
            {
                throw new FormatException($"{symbol} is referenced before it is declared.");
            }
            return value;
        }

        // ------------------------------------------------------------ readers

        private static Element[] ReadElements(
            Declaration declaration, Dictionary<string, float[]> records)
        {
            string[] entries = SplitEntries(declaration.Body);
            var elements = new Element[entries.Length];

            for (int index = 0; index < entries.Length; ++index)
            {
                string[] fields = SplitTopLevel(entries[index]);
                if (fields.Length != 5)
                {
                    throw new FormatException(
                        $"{declaration.Symbol}[{index}] has {fields.Length} fields, expected 5.");
                }

                float[] corners = ReadFloats(fields[1]);
                if (corners.Length != 18)
                {
                    throw new FormatException(
                        $"{declaration.Symbol}[{index}] has {corners.Length} gate corners, expected 18.");
                }

                float[] slice = Lookup(records, fields[3]);
                int count = int.Parse(fields[4], CultureInfo.InvariantCulture);
                if (slice.Length != count * 6)
                {
                    throw new FormatException(
                        $"{fields[3]} holds {slice.Length / 6} records, but {declaration.Symbol}" +
                        $"[{index}] declares {count}.");
                }

                elements[index] = new Element
                {
                    Name = ReadString(fields[0]),
                    Extra = ReadString(fields[2]),
                    Corners = corners,
                    Records = slice,
                };
            }
            return elements;
        }

        private static Section[] ReadSections(
            Declaration declaration,
            Dictionary<string, Element[]> elements,
            Dictionary<string, Section[]> sections,
            Dictionary<string, string[]> groups)
        {
            string[] entries = SplitEntries(declaration.Body);
            var read = new Section[entries.Length];

            for (int index = 0; index < entries.Length; ++index)
            {
                string[] fields = SplitTopLevel(entries[index]);
                if (fields.Length != 9)
                {
                    throw new FormatException(
                        $"{declaration.Symbol}[{index}] has {fields.Length} fields, expected 9.");
                }

                var section = new Section
                {
                    Elements = fields[0] == "NULL" ? null : Lookup(elements, fields[0]),
                    Start = ReadString(fields[2]),
                    End = ReadString(fields[3]),
                    Final = ReadString(fields[4]),
                    Reverse = fields[5] != "0",
                };

                if (fields[6] != "NULL")
                {
                    string[] members = Lookup(groups, fields[6]);
                    section.Alternatives = new Section[members.Length][];
                    for (int member = 0; member < members.Length; ++member)
                    {
                        section.Alternatives[member] = Lookup(sections, members[member]);
                    }
                }

                read[index] = section;
            }
            return read;
        }

        private static List<Course> ReadCourses(
            Declaration declaration, Dictionary<string, Section[]> sections)
        {
            var courses = new List<Course>();
            foreach (string entry in SplitEntries(declaration.Body))
            {
                string[] fields = SplitTopLevel(entry);
                if (fields.Length != 3)
                {
                    throw new FormatException(
                        $"COURSES[] entry has {fields.Length} fields, expected 3.");
                }

                courses.Add(new Course
                {
                    Track = ReadString(fields[0]),
                    Sections = Lookup(sections, fields[1]),
                });
            }
            return courses;
        }
    }
}
