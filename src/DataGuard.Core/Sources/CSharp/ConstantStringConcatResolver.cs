namespace DataGuard.SqlComposition;

using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

/// <summary>Shared source compiled against Core and IDE Roslyn versions; never executes application code.</summary>
internal static class ConstantStringConcatResolver
{
    internal const int MaximumTextLength = 256 * 1024;

    internal static string? Resolve(
        IInvocationOperation invocation,
        CancellationToken cancellationToken,
        int depth = 0,
        Action<string, IOperation>? appendSource = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var method = invocation.TargetMethod;
        if (depth > 10 || method.Name != "Concat" || !method.IsStatic ||
            method.ContainingType.SpecialType != SpecialType.System_String ||
            method.DeclaringSyntaxReferences.Length != 0 ||
            method.Parameters.Length is < 2 or > 4 ||
            method.Parameters.Any(parameter => parameter.Type.SpecialType != SpecialType.System_String) ||
            invocation.Arguments.Length != method.Parameters.Length ||
            invocation.Arguments.Any(argument => argument.Parameter is null))
        {
            return null;
        }

        // Parameter order, not source order, determines the result of named arguments.
        var arguments = invocation.Arguments.OrderBy(argument => argument.Parameter!.Ordinal).ToArray();
        var parts = new string[arguments.Length];
        var length = 0;
        for (var index = 0; index < arguments.Length; index++)
        {
            var value = arguments[index].Value;
            var constant = value.ConstantValue;
            var part = constant.HasValue && constant.Value is null or string
                ? constant.Value as string ?? string.Empty
                : value is IInvocationOperation nested
                    ? Resolve(nested, cancellationToken, depth + 1)
                    : null;
            if (part is null || part.Length > MaximumTextLength - length)
            {
                return null;
            }

            parts[index] = part;
            length += part.Length;
        }

        // No partial source map is published if any operand is unknown.
        if (appendSource is not null)
        {
            for (var index = 0; index < arguments.Length; index++)
            {
                if (parts[index].Length == 0)
                {
                    continue;
                }

                var value = arguments[index].Value;
                if (value is IInvocationOperation nested)
                {
                    Resolve(nested, cancellationToken, depth + 1, appendSource);
                }
                else
                {
                    appendSource(parts[index], value);
                }
            }
        }

        return string.Concat(parts);
    }
}
