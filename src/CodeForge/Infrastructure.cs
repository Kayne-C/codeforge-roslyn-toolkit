using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

namespace CodeForge
{
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {
        private readonly T[]? _items;

        public EquatableArray(IEnumerable<T> items)
        {
            _items = items.ToArray();
        }

        public int Length => _items?.Length ?? 0;

        public T this[int index] => _items![index];

        public bool Equals(EquatableArray<T> other)
        {
            var a = _items ?? Array.Empty<T>();
            var b = other._items ?? Array.Empty<T>();
            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var item in _items ?? Array.Empty<T>())
            {
                hash = unchecked(hash * 31 + item.GetHashCode());
            }

            return hash;
        }

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed record DiagnosticInfo(string Id, string Message, string FilePath, int Start, int Length, int StartLine, int StartCharacter, int EndLine, int EndCharacter)
    {
        public static DiagnosticInfo Create(string id, string message, Location location)
        {
            var span = location.GetLineSpan();
            return new DiagnosticInfo(
                id,
                message,
                span.Path ?? string.Empty,
                location.SourceSpan.Start,
                location.SourceSpan.Length,
                span.StartLinePosition.Line,
                span.StartLinePosition.Character,
                span.EndLinePosition.Line,
                span.EndLinePosition.Character);
        }

        public Diagnostic ToDiagnostic()
        {
            var descriptor = Descriptors.ById(Id);
            var location = string.IsNullOrEmpty(FilePath)
                ? Location.None
                : Location.Create(
                    FilePath,
                    new Microsoft.CodeAnalysis.Text.TextSpan(Start, Length),
                    new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                        new Microsoft.CodeAnalysis.Text.LinePosition(StartLine, StartCharacter),
                        new Microsoft.CodeAnalysis.Text.LinePosition(EndLine, EndCharacter)));
            return Diagnostic.Create(descriptor, location, Message);
        }
    }

    internal static class Descriptors
    {
        private const string Generators = "CodeForge.Generators";
        private const string Usage = "CodeForge.Usage";
        private const string Architecture = "CodeForge.Architecture";

        public static readonly DiagnosticDescriptor IdNotPartialStruct = new DiagnosticDescriptor(
            "CFG001", "StronglyTypedId target must be a partial struct", "{0}", Generators, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor IdUnsupportedBacking = new DiagnosticDescriptor(
            "CFG002", "Unsupported StronglyTypedId backing type", "{0}", Generators, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor EnumNotSupported = new DiagnosticDescriptor(
            "CFG003", "EnumExtensions target is not supported", "{0}", Generators, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor MapUnmappedMember = new DiagnosticDescriptor(
            "CFG101", "Destination member has no source", "{0}", Generators, DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor MapIncompatible = new DiagnosticDescriptor(
            "CFG102", "Members cannot be mapped", "{0}", Generators, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor MapNullability = new DiagnosticDescriptor(
            "CFG103", "Nullable source is mapped to a non-nullable destination", "{0}", Generators, DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor MapUnconstructable = new DiagnosticDescriptor(
            "CFG104", "Destination cannot be constructed", "{0}", Generators, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor SyncOverAsync = new DiagnosticDescriptor(
            "CF0002", "Do not block on asynchronous code",
            "'{0}' blocks the calling thread on a Task; await it instead",
            Usage, DiagnosticSeverity.Warning, true,
            "Blocking on a Task risks deadlocks and thread-pool starvation.");

        public static readonly DiagnosticDescriptor PreferTimeProvider = new DiagnosticDescriptor(
            "CF0003", "Prefer TimeProvider over the ambient clock",
            "'{0}' reads the ambient clock; inject a TimeProvider so the code can be tested",
            Usage, DiagnosticSeverity.Info, true);

        public static readonly DiagnosticDescriptor NewHttpClient = new DiagnosticDescriptor(
            "CF0004", "Do not create HttpClient per call",
            "Creating an HttpClient here can exhaust sockets; use IHttpClientFactory or a shared instance",
            Usage, DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor AsyncVoid = new DiagnosticDescriptor(
            "CF0005", "Avoid async void",
            "'{0}' is async void, so exceptions cannot be observed by callers; return Task instead",
            Usage, DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor BlockingSleep = new DiagnosticDescriptor(
            "CF0006", "Do not block with Thread.Sleep in an async method",
            "'Thread.Sleep' blocks the thread inside an async method; use 'await Task.Delay' instead",
            Usage, DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor ForbiddenDependency = new DiagnosticDescriptor(
            "CF0100", "Forbidden architectural dependency",
            "'{0}' must not depend on '{1}' (rule: {2} -> {3})",
            Architecture, DiagnosticSeverity.Error, true);

        public static readonly DiagnosticDescriptor InvalidRule = new DiagnosticDescriptor(
            "CF0101", "Invalid ForbidDependency rule",
            "{0}",
            Architecture, DiagnosticSeverity.Warning, true);

        private static readonly Dictionary<string, DiagnosticDescriptor> All = new[]
        {
            IdNotPartialStruct, IdUnsupportedBacking, EnumNotSupported, MapUnmappedMember, MapIncompatible, MapNullability, MapUnconstructable,
        }.ToDictionary(d => d.Id);

        public static DiagnosticDescriptor ById(string id) => All[id];
    }
}
