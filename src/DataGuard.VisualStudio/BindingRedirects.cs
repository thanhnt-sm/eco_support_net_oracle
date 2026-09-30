// <copyright file="BindingRedirects.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

using Microsoft.VisualStudio.Shell;

// The 10.0.0.x redirects below must equal the assembly version shipped by the System.Text.Json
// PackageReference in DataGuard.VisualStudio.csproj (package 10.0.N -> assembly 10.0.0.N); CreatePkgDef
// (Microsoft.VSSDK.BuildTools) rejects a NewVersion that does not match the built DLL.
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.Text.Json",
    PublicKeyToken = "cc7b13ffcd2ddd51",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "10.0.0.12",
    NewVersion = "10.0.0.12")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.Text.Encodings.Web",
    PublicKeyToken = "cc7b13ffcd2ddd51",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "10.0.0.12",
    NewVersion = "10.0.0.12")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "Microsoft.Bcl.AsyncInterfaces",
    PublicKeyToken = "cc7b13ffcd2ddd51",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "10.0.0.12",
    NewVersion = "10.0.0.12")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.IO.Pipelines",
    PublicKeyToken = "cc7b13ffcd2ddd51",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "10.0.0.12",
    NewVersion = "10.0.0.12")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.Memory",
    PublicKeyToken = "cc7b13ffcd2ddd51",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "4.0.5.0",
    NewVersion = "4.0.5.0")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.Buffers",
    PublicKeyToken = "cc7b13ffcd2ddd51",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "4.0.5.0",
    NewVersion = "4.0.5.0")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.Runtime.CompilerServices.Unsafe",
    PublicKeyToken = "b03f5f7f11d50a3a",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "6.0.3.0",
    NewVersion = "6.0.3.0")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.Numerics.Vectors",
    PublicKeyToken = "b03f5f7f11d50a3a",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "4.1.6.0",
    NewVersion = "4.1.6.0")]
[assembly: ProvideBindingRedirection(
    AssemblyName = "System.Threading.Tasks.Extensions",
    PublicKeyToken = "cc7b13ffcd2ddd51",
    Culture = "neutral",
    OldVersionLowerBound = "0.0.0.0",
    OldVersionUpperBound = "4.2.4.0",
    NewVersion = "4.2.4.0")]
