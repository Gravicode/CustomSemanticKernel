﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using Microsoft.SemanticKernel.Diagnostics;

namespace Microsoft.SemanticKernel.AI.ChatCompletion;

/// <summary>
/// A description of the intended purpose of a message within a chat completions interaction.
/// </summary>
public readonly struct AuthorRole : IEquatable<AuthorRole>
{
    /// <summary>
    /// The role that instructs or sets the behavior of the assistant.
    /// </summary>
    public static readonly AuthorRole System = new("System");
    /// <summary>
    /// The role that provides responses to system-instructed, user-prompted input.
    /// </summary>
    public static readonly AuthorRole Assistant = new("Assistant");
    /// <summary>
    /// The role that provides input for chat completions.
    /// </summary>
    public static readonly AuthorRole User = new("User");

    /// <summary>
    /// Gets the label associated with this AuthorRole.
    /// </summary>
    /// <remarks>
    /// The label is what will be serialized into the "role" message field of the Chat Message format.
    /// </remarks>
    public string Label { get; }

    /// <summary>
    /// Creates a new AuthorRole instance with the provided label.
    /// </summary>
    /// <param name="label"></param>
    public AuthorRole(string label)
    {
        Verify.NotNull(label, nameof(label));
        this.Label = label!;
    }

    /// <summary>
    /// Returns a value indicating whether two AuthorRole instances are equivalent, as determined by a
    /// case-insensitive comparison of their labels.
    /// </summary>
    /// <param name="left"> the first AuthorRole instance to compare </param>
    /// <param name="right"> the second AuthorRole instance to compare </param>
    /// <returns> true if left and right are both null or have equivalent labels; false otherwise </returns>
    public static bool operator ==(AuthorRole left, AuthorRole right)
    {
        if (Object.ReferenceEquals(left, right))
        {
            return true;
        }

        if (Object.ReferenceEquals(left, null) || Object.ReferenceEquals(right, null))
        {
            return false;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Returns a value indicating whether two AuthorRole instances are not equivalent, as determined by a
    /// case-insensitive comparison of their labels.
    /// </summary>
    /// <param name="left"> the first AuthorRole instance to compare </param>
    /// <param name="right"> the second AuthorRole instance to compare </param>
    /// <returns> false if left and right are both null or have equivalent labels; true otherwise </returns>
    public static bool operator !=(AuthorRole left, AuthorRole right)
        => !(left == right);

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object obj)
        => obj is AuthorRole otherRole && this == otherRole;

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode()
        => this.Label.GetHashCode();

    /// <inheritdoc/>
    public bool Equals(AuthorRole other)
        => !Object.ReferenceEquals(other, null)
            && string.Equals(this.Label, other.Label, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string ToString() => this.Label;
}
