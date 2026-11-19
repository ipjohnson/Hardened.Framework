using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.YamlReader;
using SharpYaml.Serialization;

namespace Hardened.OpenApi.SourceGenerator;

/// <summary>
/// The descriptions one project names, so a <c>$ref</c> in one of them can name a component that
/// another declares.
/// </summary>
/// <remarks>
/// <para>
/// Resolved on the document rather than on the object model. Each component a reference names is
/// copied from the other file into the referencing file's own <c>components</c>, with whatever it
/// references in turn, and the reference is rewritten to the local
/// <c>#/components/&lt;kind&gt;/&lt;name&gt;</c> form. The parser then reads an ordinary
/// self-contained document. A copied schema is parsed from the same text as the file that declares
/// it, so <c>SharedSchemas</c> finds the two alike and the type is written once.
/// </para>
/// <para>
/// The path before <c>#</c> is relative to the file that holds the reference, as OpenAPI gives.
/// A file the project does not name is not read. With <c>LoadExternalRefs</c> on, such a reference
/// is left for the reader, which is what that property is for.
/// </para>
/// <para>
/// A response is the one component that is refused. Each file generates the error type of a named
/// response it uses, so two files using one shared response would write that type twice.
/// </para>
/// </remarks>
internal sealed class ProjectDocuments
{
    /// <summary>A <c>$ref</c> whose value does not start with <c>#</c>.</summary>
    private static readonly Regex OtherFileReference = new(
        @"\$ref[""']?\s*:\s*[""']?(?![""'#\s])",
        RegexOptions.CultureInvariant
    );

    private readonly Dictionary<string, Func<string>> _sources = new(StringComparer.Ordinal);

    private readonly Dictionary<string, JsonObject?> _read = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _unreadable = new(StringComparer.Ordinal);

    private readonly bool _leaveOtherFilesToReader;

    /// <param name="documents">Each description's path and a way to read its text.</param>
    /// <param name="leaveOtherFilesToReader">
    /// Whether a reference to a file the project does not name is left for the reader to follow,
    /// as <c>LoadExternalRefs</c> asks, rather than reported.
    /// </param>
    public ProjectDocuments(
        IEnumerable<KeyValuePair<string, Func<string>>> documents,
        bool leaveOtherFilesToReader = false
    )
    {
        foreach (var document in documents)
        {
            _sources[Path.GetFullPath(document.Key)] = document.Value;
        }

        _leaveOtherFilesToReader = leaveOtherFilesToReader;
    }

    /// <summary>What <see cref="Resolve"/> did to one description.</summary>
    public sealed class Resolution
    {
        /// <summary>The document to read instead, as JSON, or null when nothing changed.</summary>
        public string? Text { get; set; }

        /// <summary>One message per reference that did not resolve.</summary>
        public List<string> Problems { get; } = new();

        /// <summary>The other files the description itself names, as full paths.</summary>
        public List<string> ReferencedFiles { get; } = new();
    }

    /// <summary>
    /// Copies every component <paramref name="text"/> names in another file into it.
    /// </summary>
    public Resolution Resolve(string text, string specPath)
    {
        var resolution = new Resolution();

        if (!OtherFileReference.IsMatch(text))
        {
            return resolution;
        }

        JsonObject? document;

        try
        {
            document = Read(text);
        }
        catch (Exception)
        {
            // The reader reads it next and says what is wrong with it.
            return resolution;
        }

        if (document == null)
        {
            return resolution;
        }

        var run = new Run(this, document, Path.GetFullPath(specPath), resolution);

        run.Resolve();

        if (run.Changed)
        {
            resolution.Text = document.ToJsonString();
        }

        return resolution;
    }

    /// <summary>
    /// The document as the reader sees it. The YAML reader converts with the same
    /// <see cref="YamlConverter"/>, so a component copied from here parses as it does in its own
    /// file.
    /// </summary>
    private static JsonObject? Read(string text)
    {
        if (OpenApiSpecParser.DetectFormat(text) == OpenApiConstants.Json)
        {
            return JsonNode.Parse(text) as JsonObject;
        }

        var stream = new YamlStream();

        stream.Load(new StringReader(text));

        return stream.Documents.Count == 0
            ? null
            : YamlConverter.ToJsonNode(stream.Documents[0]) as JsonObject;
    }

    private bool Names(string path) => _sources.ContainsKey(path);

    /// <summary>Another description of the project, read once however many files name it.</summary>
    private JsonObject? Load(string path, out string? error)
    {
        error = null;

        if (_unreadable.TryGetValue(path, out var failed))
        {
            error = failed;
            return null;
        }

        if (_read.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var document = Read(_sources[path]());

            _read[path] = document;

            if (document == null)
            {
                error = "it holds no document";
            }

            return document;
        }
        catch (Exception exception)
        {
            _unreadable[path] = exception.Message;
            error = exception.Message;

            return null;
        }
    }

    /// <summary>One description's references, followed to every component they reach.</summary>
    private sealed class Run
    {
        private readonly ProjectDocuments _project;
        private readonly JsonObject _document;
        private readonly string _specPath;
        private readonly Resolution _resolution;
        private readonly Queue<Import> _pending = new();
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _placedBy = new(StringComparer.Ordinal);

        public Run(
            ProjectDocuments project,
            JsonObject document,
            string specPath,
            Resolution resolution
        )
        {
            _project = project;
            _document = document;
            _specPath = specPath;
            _resolution = resolution;
        }

        public bool Changed { get; private set; }

        /// <summary>
        /// Rewrites the description's own references first and copies components after. Every
        /// rewrite is a string, so a component the description declares and one copied in are
        /// compared in the same form.
        /// </summary>
        public void Resolve()
        {
            foreach (var reference in References(_document))
            {
                Follow(reference.Key, reference.Value, _specPath, fromDescription: true);
            }

            while (_pending.Count > 0)
            {
                Copy(_pending.Dequeue());
            }
        }

        private void Follow(JsonObject owner, string reference, string holder, bool fromDescription)
        {
            var hash = reference.IndexOf('#');
            var filePart = hash < 0 ? reference : reference.Substring(0, hash);
            var fragment = hash < 0 ? "" : reference.Substring(hash + 1);
            var component = Component(fragment);

            string file;

            if (filePart.Length == 0)
            {
                // The description's own references are already local.
                if (fromDescription)
                {
                    return;
                }

                file = holder;
            }
            else
            {
                var located = Locate(filePart, holder);

                if (located == null || !_project.Names(located))
                {
                    if (!_project._leaveOtherFilesToReader)
                    {
                        Report(
                            "'{0}' references '{1}', and '{2}' is not a HardenedOpenApiSpec of "
                                + "this project, so {3} cannot be resolved. A $ref to another file "
                                + "resolves only to a description the same project names. Add the "
                                + "file as a HardenedOpenApiSpec, or declare {3} in '{0}'.",
                            holder,
                            reference,
                            located ?? filePart,
                            component == null
                                ? "it"
                                : $"the {Singular(component.Value.Kind)} '{component.Value.Name}'"
                        );
                    }

                    return;
                }

                file = located;
            }

            if (component == null)
            {
                Report(
                    "'{0}' references '{1}', which names no entry under '#/components/'. A $ref "
                        + "to another file can name a component and nothing else.",
                    holder,
                    reference
                );

                return;
            }

            var (kind, name) = component.Value;
            var isOtherFile = !string.Equals(file, _specPath, StringComparison.Ordinal);

            if (isOtherFile && kind == "responses")
            {
                Report(
                    "'{0}' references '{1}', a response in another file, which is not supported. "
                        + "Each file that used it would generate the response's error type, and the "
                        + "two would collide. Declare the response in '{0}', and give it the schema "
                        + "'{2}' declares through a $ref under its content.",
                    holder,
                    reference,
                    file
                );

                return;
            }

            // Checked before the reference is rewritten, because a rewritten reference that names
            // nothing reads as the description's own mistake.
            if (isOtherFile)
            {
                if (!Declares(file, kind, name, reference, holder))
                {
                    return;
                }
            }
            else if (Section(_document, kind)?[name] == null)
            {
                Missing(holder, reference, file, kind, name);

                return;
            }

            if (filePart.Length > 0)
            {
                owner["$ref"] = "#" + fragment;
                Changed = true;
            }

            if (!isOtherFile)
            {
                return;
            }

            if (
                fromDescription
                && !_resolution.ReferencedFiles.Contains(file, StringComparer.Ordinal)
            )
            {
                _resolution.ReferencedFiles.Add(file);
            }

            _pending.Enqueue(new Import(file, kind, name, reference, holder));
        }

        /// <summary>
        /// Puts one component of another file into the description, after following its own
        /// references.
        /// </summary>
        private void Copy(Import import)
        {
            if (!_visited.Add(import.File + "\u0000" + import.Kind + "\u0000" + import.Name))
            {
                return;
            }

            // Follow has already checked that the file declares it.
            var copy = Section(_project.Load(import.File, out _)!, import.Kind)![
                import.Name
            ]!.DeepClone();

            foreach (var reference in References(copy))
            {
                Follow(reference.Key, reference.Value, import.File, fromDescription: false);
            }

            var components = Child(_document, "components");
            var section = Child(components, import.Kind);
            var key = import.Kind + "\u0000" + import.Name;

            if (section[import.Name] is { } existing)
            {
                if (!JsonNode.DeepEquals(existing, copy))
                {
                    Report(
                        "'{0}' references '{1}', and the {2} '{3}' that '{4}' declares differs "
                            + "from the '{3}' that '{5}' declares. Every description of a project "
                            + "generates into one models namespace, so the two have to be declared "
                            + "alike, or one of them renamed.",
                        import.Holder,
                        import.Reference,
                        Singular(import.Kind),
                        import.Name,
                        import.File,
                        _placedBy.TryGetValue(key, out var placed) ? placed : _specPath
                    );
                }

                return;
            }

            section[import.Name] = copy;
            _placedBy[key] = import.File;
            Changed = true;
        }

        /// <summary>Whether <paramref name="file"/> declares the component, reporting why not.</summary>
        private bool Declares(
            string file,
            string kind,
            string name,
            string reference,
            string holder
        )
        {
            var source = _project.Load(file, out var error);

            if (source == null)
            {
                Report(
                    "'{0}' references '{1}', and '{2}' could not be read: {3}",
                    holder,
                    reference,
                    file,
                    error ?? ""
                );

                return false;
            }

            if (Section(source, kind)?[name] == null)
            {
                Missing(holder, reference, file, kind, name);

                return false;
            }

            return true;
        }

        private void Missing(string holder, string reference, string file, string kind, string name)
        {
            Report(
                "'{0}' references '{1}', and '{2}' declares no {3} '{4}'.",
                holder,
                reference,
                file,
                Singular(kind),
                name
            );
        }

        private void Report(string format, params object[] args)
        {
            var message = string.Format(CultureInfo.InvariantCulture, format, args);

            if (_reported.Add(message))
            {
                _resolution.Problems.Add(message);
            }
        }
    }

    private sealed class Import
    {
        public Import(string file, string kind, string name, string reference, string holder)
        {
            File = file;
            Kind = kind;
            Name = name;
            Reference = reference;
            Holder = holder;
        }

        public string File { get; }

        public string Kind { get; }

        public string Name { get; }

        /// <summary>The reference as written, for messages.</summary>
        public string Reference { get; }

        /// <summary>The file that holds the reference.</summary>
        public string Holder { get; }
    }

    /// <summary>
    /// The file a reference names, relative to the file that holds it, or null for a URL.
    /// </summary>
    private static string? Locate(string filePart, string holder)
    {
        if (
            filePart.IndexOf("://", StringComparison.Ordinal) >= 0
            || filePart.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        return Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(holder) ?? "", Uri.UnescapeDataString(filePart))
        );
    }

    /// <summary>
    /// The kind and name of <c>/components/&lt;kind&gt;/&lt;name&gt;</c>, or null for any other
    /// pointer.
    /// </summary>
    private static (string Kind, string Name)? Component(string fragment)
    {
        var segments = fragment.Split('/');

        if (
            segments.Length != 4
            || segments[0].Length != 0
            || segments[1] != "components"
            || segments[2].Length == 0
            || segments[3].Length == 0
        )
        {
            return null;
        }

        var name = Uri.UnescapeDataString(segments[3]).Replace("~1", "/").Replace("~0", "~");

        return (segments[2], name);
    }

    private static string Singular(string kind) =>
        kind switch
        {
            "schemas" => "schema",
            "responses" => "response",
            "parameters" => "parameter",
            "requestBodies" => "request body",
            "headers" => "header",
            "examples" => "example",
            "links" => "link",
            "callbacks" => "callback",
            "pathItems" => "path item",
            "securitySchemes" => "security scheme",
            _ => kind,
        };

    private static JsonObject? Section(JsonObject document, string kind) =>
        document["components"] is JsonObject components ? components[kind] as JsonObject : null;

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();

        parent[name] = created;

        return created;
    }

    /// <summary>
    /// Every <c>$ref</c> under <paramref name="node"/>, with the object that holds it, in document
    /// order. Collected before any is rewritten, because copying a component adds to the tree.
    /// </summary>
    private static List<KeyValuePair<JsonObject, string>> References(JsonNode? node)
    {
        var found = new List<KeyValuePair<JsonObject, string>>();

        Collect(node, found);

        return found;
    }

    private static void Collect(JsonNode? node, List<KeyValuePair<JsonObject, string>> found)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    if (
                        property.Key == "$ref"
                        && property.Value is JsonValue value
                        && value.TryGetValue<string>(out var reference)
                    )
                    {
                        found.Add(new KeyValuePair<JsonObject, string>(obj, reference));
                    }
                    else
                    {
                        Collect(property.Value, found);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item, found);
                }

                break;
        }
    }
}
