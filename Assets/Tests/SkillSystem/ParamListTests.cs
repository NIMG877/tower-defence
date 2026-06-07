using NUnit.Framework;
using SkillSystem;
using UnityEngine;

public class ParamListTests
{
    [Test]
    public void GetTyped_Int_Float_Bool_String()
    {
        var pl = new ParamList
        {
            entries = new[] {
                new ParamEntry { key = "i", type = ParamValueType.Int, value = "42" },
                new ParamEntry { key = "f", type = ParamValueType.Float, value = "3.14" },
                new ParamEntry { key = "b", type = ParamValueType.Bool, value = "true" },
                new ParamEntry { key = "s", type = ParamValueType.String, value = "zombie" },
            }
        };
        Assert.AreEqual(42, pl.GetInt("i"));
        Assert.AreEqual(3.14f, pl.GetFloat("f"), 0.001f);
        Assert.IsTrue(pl.GetBool("b"));
        Assert.AreEqual("zombie", pl.GetString("s"));
    }

    [Test]
    public void GetTyped_MissingKey_ReturnsDefault()
    {
        var pl = new ParamList();
        Assert.AreEqual(0, pl.GetInt("nope"));
        Assert.AreEqual(0f, pl.GetFloat("nope"));
        Assert.IsFalse(pl.GetBool("nope"));
        Assert.AreEqual(string.Empty, pl.GetString("nope"));
    }

    [Test]
    public void HasKey_TrueOnlyIfPresent()
    {
        var pl = new ParamList
        {
            entries = new[] { new ParamEntry { key = "x", type = ParamValueType.Int, value = "1" } }
        };
        Assert.IsTrue(pl.HasKey("x"));
        Assert.IsFalse(pl.HasKey("y"));
    }
}
