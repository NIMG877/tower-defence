using NUnit.Framework;
using SkillSystem;

public class BlackboardTests
{
    [Test]
    public void Get_DefaultValue_WhenKeyMissing()
    {
        var bb = new Blackboard();
        Assert.AreEqual(0, bb.Get<int>("missing"));
        Assert.AreEqual(0f, bb.Get<float>("missing"));
        Assert.AreEqual("default", bb.Get<string>("missing", "default"));
    }

    [Test]
    public void Set_ThenGet_ReturnsValue()
    {
        var bb = new Blackboard();
        bb.Set("hp", 100);
        bb.Set("name", "zombie");
        Assert.AreEqual(100, bb.Get<int>("hp"));
        Assert.AreEqual("zombie", bb.Get<string>("name"));
    }

    [Test]
    public void Has_TrueAfterSet_FalseAfterRemove()
    {
        var bb = new Blackboard();
        Assert.IsFalse(bb.Has("x"));
        bb.Set("x", 1);
        Assert.IsTrue(bb.Has("x"));
        bb.Remove("x");
        Assert.IsFalse(bb.Has("x"));
    }
}
