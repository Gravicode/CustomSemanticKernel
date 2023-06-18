﻿// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.SemanticKernel.SkillDefinition;

/// <summary>
/// Debugger type proxy for <see cref="SkillCollection"/> and <see cref="ReadOnlySkillCollection"/>.
/// </summary>
// ReSharper disable once InconsistentNaming
internal sealed class IReadOnlySkillCollectionTypeProxy
{
    private readonly IReadOnlySkillCollection _collection;

    public IReadOnlySkillCollectionTypeProxy(IReadOnlySkillCollection collection) => this._collection = collection;

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public SkillProxy[] Items
    {
        get
        {
            var view = this._collection.GetFunctionsView();
            return view.NativeFunctions
                .Concat(view.SemanticFunctions)
                .GroupBy(f => f.Key)
                .Select(g => new SkillProxy(g.SelectMany(f => f.Value)) { Name = g.Key })
                .ToArray();
        }
    }

    [DebuggerDisplay("{Name}")]
    public sealed class SkillProxy : List<FunctionView>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public string? Name;

        public SkillProxy(IEnumerable<FunctionView> functions) : base(functions) { }
    }
}
