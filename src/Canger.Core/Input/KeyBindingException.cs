// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.Core.Input;

/// <summary>Raised when a key binding directive cannot be carried out.</summary>
public sealed class KeyBindingException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public KeyBindingException(string message) : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public KeyBindingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with no detail. Prefer the descriptive overloads.</summary>
    public KeyBindingException() : base("Invalid key binding.")
    {
    }
}
