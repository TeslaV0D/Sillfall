using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Weatherly.Core.Rendering;

namespace Weatherly.Core.Tests.Rendering;

public class RenderFrameTests
{
    [Fact]
    public void Sprite_HasTheLayoutTheShaderExpects()
    {
        Unsafe.SizeOf<Sprite>().Should().Be(Sprite.SizeInBytes);
        Marshal.SizeOf<Sprite>().Should().Be(Sprite.SizeInBytes);
        Marshal.OffsetOf<Sprite>("<Rotation>k__BackingField").ToInt32().Should().Be(16);
        Marshal.OffsetOf<Sprite>("<Color>k__BackingField").ToInt32().Should().Be(24);
    }

    [Fact]
    public void Premultiply_ScalesTheColorByAlpha()
    {
        Sprite.Premultiply(new Vector3(1f, 0.5f, 0f), 0.5f).Should().Be(new Vector4(0.5f, 0.25f, 0f, 0.5f));
    }

    [Fact]
    public void Add_KeepsTheOrderAndGrows()
    {
        var frame = new RenderFrame(initialCapacity: 16);
        for (int i = 0; i < 40; i++)
        {
            frame.Add(new Sprite(new Vector2(i, 0), Vector2.One, 0f, SpriteShape.Disc, Vector4.One));
        }

        frame.SpriteCount.Should().Be(40);
        frame.Sprites[39].Center.X.Should().Be(39);
    }

    [Fact]
    public void Clear_EmptiesTheFrame()
    {
        var frame = new RenderFrame();
        frame.Add(default);

        frame.Clear();

        frame.SpriteCount.Should().Be(0);
        frame.Surfaces.Should().BeNull();
    }
}
