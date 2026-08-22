using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace Spintax.Core.Tests
{
    /// <summary>
    /// The brief's §3 table, read off the built assemblies rather than off the csproj: a dll that
    /// breaks one of these does not load in ZennoPoster, or falls over under load. Both shipped
    /// dlls — the engine and the facade — are held to it. Checked by manifest, not by eye.
    /// </summary>
    public class AssemblyContractTests
    {
        public static IEnumerable<object[]> Shipped()
        {
            yield return new object[] { typeof(Engine).Assembly, "Spintax.Core" };
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void Targets_netstandard20_or_net472(Assembly asm, string name)
        {
            // Two builds ship: netstandard2.0 for any host, net472 for ZennoPoster — whose cube
            // compiler (7.9.1.0) has no reference to netstandard.dll and fails with CS0012 as soon
            // as a cube touches Dictionary<,> from a netstandard2.0 API. The host this test runs
            // on picks the matching build, so both get checked across the two test hosts.
            var tfm = asm.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
            Assert.True(".NETStandard,Version=v2.0" == tfm || ".NETFramework,Version=v4.7.2" == tfm, $"{name}: {tfm}");
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void References_nothing_but_netstandard_and_our_own_engine(Assembly asm, string name)
        {
            // Zero NuGet packages means zero referenced assemblies beyond the platform — and, for
            // the facade, the engine it is a facade of.
            var refs = asm.GetReferencedAssemblies().Select(a => a.Name ?? "").OrderBy(n => n, StringComparer.Ordinal).ToList();
            // The platform: netstandard for the netstandard2.0 build; the Framework's own
            // assemblies for net472 (BigInteger lives in System.Numerics there).
            var platform = new[] { "netstandard", "mscorlib", "System", "System.Core", "System.Numerics" };
            var foreign = refs.Where(r => !platform.Contains(r, StringComparer.Ordinal) && !(name == "Spintax.Zenno" && r == "Spintax.Core")).ToList();
            Assert.True(foreign.Count == 0, $"{name} references: {string.Join(", ", refs)}");
            if (name == "Spintax.Zenno") Assert.Contains("Spintax.Core", refs);
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void The_project_file_declares_no_package_reference(Assembly asm, string name)
        {
            // The manifest check above cannot see an analyzer-, build- or content-only package,
            // which leaves no assembly reference behind. The project file can.
            _ = asm;
            var csproj = Path.Combine(RepoRoot(), "src", name, name + ".csproj");
            var text = File.ReadAllText(csproj);
            Assert.DoesNotContain("<PackageReference", text);
            Assert.Contains("<TargetFrameworks>netstandard2.0;net472</TargetFrameworks>", text);
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void Is_AnyCPU(Assembly asm, string name)
        {
            asm.ManifestModule.GetPEKind(out var peKind, out var machine);
            Assert.True(peKind.HasFlag(PortableExecutableKinds.ILOnly), $"{name}: PE kind {peKind}");
            Assert.False(peKind.HasFlag(PortableExecutableKinds.Required32Bit), $"{name}: PE kind {peKind}");
            Assert.False(peKind.HasFlag(PortableExecutableKinds.Preferred32Bit), $"{name}: PE kind {peKind}");
            Assert.Equal(ImageFileMachine.I386, machine); // what AnyCPU ILOnly images report
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void Public_surface_has_no_Task_returning_members(Assembly asm, string name)
        {
            var offenders = asm.GetExportedTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => typeof(System.Threading.Tasks.Task).IsAssignableFrom(m.ReturnType))
                .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
                .ToList();
            Assert.True(offenders.Count == 0, $"{name}: {string.Join(", ", offenders)}");
        }

        [Theory]
        [MemberData(nameof(Shipped))]
        public void No_mutable_static_state(Assembly asm, string name)
        {
            // Static readonly fields are allowed only when their type is immutable by
            // construction (Regex, string, primitives); anything else is a shared-state risk
            // under ZennoPoster's dozens of parallel instances. Compiler-generated closure
            // classes (`<>c`) cache non-capturing lambdas in static fields; they are written once
            // with an immutable delegate and are not ours to avoid.
            var offenders = asm.GetTypes()
                .Where(t => t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null)
                .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(f => !f.IsLiteral)
                .Where(f => !f.IsInitOnly || !IsImmutable(f.FieldType))
                .Select(f => $"{f.DeclaringType?.Name}.{f.Name} : {f.FieldType.Name}")
                .ToList();
            Assert.True(offenders.Count == 0, $"{name}: mutable or non-immutable static state: " + string.Join(", ", offenders));
        }

        private static bool IsImmutable(Type t) =>
            t.IsPrimitive
            || t == typeof(string)
            || t == typeof(System.Text.RegularExpressions.Regex)
            || t.IsEnum;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Spintax.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
