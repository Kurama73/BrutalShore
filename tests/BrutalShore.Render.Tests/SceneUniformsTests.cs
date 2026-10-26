using System.Reflection;
using System.Text.RegularExpressions;

namespace BrutalShore.Render.Tests;

/// <summary>
/// Checks that the shaders and the C# struct still describe the same block of memory.
/// </summary>
/// <remarks>
/// <para>
/// The uniform block is written twice — once as a struct and once at the top of every shader that
/// reads it — because shaders are compiled from source at startup with no include path. There is
/// nowhere to put one shared declaration, so the only thing left is to check the copies against
/// each other.
/// </para>
/// <para>
/// It is worth checking because the failure is quiet. Two members swapped is not a crash and not
/// a validation error: it is a picture that looks merely wrong, and it has already happened once
/// here, with the shadow terms and the underwater tint reading each other's values.
/// </para>
/// </remarks>
public sealed partial class SceneUniformsTests
{
    [Theory]
    [InlineData("voxel.vert")]
    [InlineData("voxel.frag")]
    [InlineData("sky.vert")]
    [InlineData("sky.frag")]
    [InlineData("shadow.vert")]
    public void every_shader_declares_the_block_the_way_the_struct_lays_it_out(string shader)
    {
        var declared = MembersIn(Read(shader));
        var actual = Fields();

        // A shader may stop short — it only has to declare as far as the members it reads, since
        // what matters is that the offsets of those agree. It may not reorder or rename.
        Assert.NotEmpty(declared);
        Assert.Equal(actual.Take(declared.Count), declared);
    }

    [Fact]
    public void at_least_one_shader_declares_the_whole_block()
    {
        // Otherwise a member could be added to the struct and read by nothing, and the test above
        // would be satisfied by every shader stopping short of it.
        var longest = new[] { "voxel.vert", "voxel.frag", "sky.vert", "sky.frag", "shadow.vert" }
            .Select(shader => MembersIn(Read(shader)).Count)
            .Max();

        Assert.Equal(Fields().Count, longest);
    }

    [Fact]
    public void every_member_is_a_whole_number_of_sixteen_byte_rows()
    {
        // What makes the plain sequential C# layout and std140 agree at all. A float or a float3
        // added to the struct would be padded differently by the two and silently shift
        // everything after it.
        foreach (var field in typeof(SceneUniforms).GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            var size = System.Runtime.InteropServices.Marshal.SizeOf(field.FieldType);
            Assert.True(size % 16 == 0, $"{field.Name} is {size} bytes, which std140 would pad");
        }
    }

    /// <summary>The struct's fields, lower-cased, in declaration order.</summary>
    private static List<string> Fields() =>
        typeof(SceneUniforms)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(field => char.ToLowerInvariant(field.Name[0]) + field.Name[1..])
            .ToList();

    /// <summary>The names declared in a shader's uniform block, in order.</summary>
    private static List<string> MembersIn(string source)
    {
        var block = BlockPattern().Match(source);
        Assert.True(block.Success, "the shader has no Scene uniform block");

        return MemberPattern()
            .Matches(block.Groups[1].Value)
            .Select(member => member.Groups[1].Value)
            .ToList();
    }

    private static string Read(string shader)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Shaders", shader);
        Assert.True(File.Exists(path), $"'{path}' is not where the shaders were expected");
        return File.ReadAllText(path);
    }

    [GeneratedRegex(@"uniform\s+Scene\s*\{(.*?)\}\s*scene\s*;", RegexOptions.Singleline)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"(?:mat4|vec4)\s+(\w+)\s*(?:\[\s*\d+\s*\])?\s*;")]
    private static partial Regex MemberPattern();
}
