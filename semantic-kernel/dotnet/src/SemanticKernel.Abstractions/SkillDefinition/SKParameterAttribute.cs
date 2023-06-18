﻿// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.SemanticKernel.SkillDefinition;

/// <summary>
/// Attribute to describe additional parameters used by a native function that aren't part of its method signature.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class SKParameterAttribute : Attribute
{
    public SKParameterAttribute(string name, string description)
    {
        this.Name = name;
        this.Description = description;
    }

    /// <summary>
    /// Gets or sets the name of the parameter.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the context parameter description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets or sets the default value of the parameter to use if no context variable is supplied matching the parameter name.
    /// </summary>
    /// <remarks>
    /// There are two ways to supply a default value to a parameter. A default value can be supplied for the parameter in
    /// the method signature itself, or a default value can be specified using this property. If both are specified, the
    /// value in the attribute is used.  The attribute is most useful when the target parameter is followed by a non-optional
    /// parameter (such that this parameter isn't permitted to be optional) or when the attribute is applied to a method
    /// to indicate a context parameter that is not specified as a method parameter but that's still used by the method body.
    /// </remarks>
    public string? DefaultValue { get; set; }
}
