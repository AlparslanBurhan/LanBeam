using LanBeam.Core.Models;

namespace LanBeam.Tests;

public class AvatarTagsTests
{
    [Fact]
    public void PresetEtiketi_DogruCozulur()
    {
        Assert.True(AvatarTags.TryGetPreset("preset:7", out int id));
        Assert.Equal(7, id);
        Assert.False(AvatarTags.TryGetPreset("img:ABCD1234", out _));
        Assert.False(AvatarTags.TryGetPreset(null, out _));
        Assert.False(AvatarTags.TryGetPreset("preset:abc", out _));
    }

    [Fact]
    public void PresetSiniriDisi_ModAlinir()
    {
        Assert.True(AvatarTags.TryGetPreset("preset:99", out int id));
        Assert.InRange(id, 0, AvatarTags.PresetCount - 1);
    }

    [Fact]
    public void VarsayilanAvatar_AyniCihazIcinKararli()
    {
        string a1 = AvatarTags.DefaultFor("cihaz-abc");
        string a2 = AvatarTags.DefaultFor("cihaz-abc");
        Assert.Equal(a1, a2);
        Assert.True(AvatarTags.TryGetPreset(a1, out _));
    }

    [Fact]
    public void GoruntuEtiketi_HashIcerir()
    {
        byte[] png = [1, 2, 3, 4, 5];
        string tag = AvatarTags.ForImageBytes(png);
        Assert.StartsWith("img:", tag);
        Assert.Equal(12, tag.Length); // "img:" + 8 hex
        Assert.True(AvatarTags.IsImage(tag));
    }
}
