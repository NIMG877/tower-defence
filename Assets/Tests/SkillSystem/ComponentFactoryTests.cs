using NUnit.Framework;
using SkillSystem;

public class ComponentFactoryTests
{
    public class StubComponent : ISkillComponent
    {
        public void OnInit(SkillContext ctx, ParamList parameters) { }
        public void OnTrigger(SkillContext ctx) { }
        public void OnTick(SkillContext ctx, float dt) { }
        public void OnTeardown(SkillContext ctx) { }
    }

    [Test]
    public void Register_ThenCreate_ReturnsInstance()
    {
        ComponentFactory.Register("Stub", () => new StubComponent());
        var c = ComponentFactory.Create("Stub");
        Assert.IsNotNull(c);
        Assert.IsInstanceOf<StubComponent>(c);
    }

    [Test]
    public void Create_UnknownType_ReturnsNull()
    {
        var c = ComponentFactory.Create("DoesNotExist");
        Assert.IsNull(c);
    }

    [Test]
    public void IsRegistered_ReflectsState()
    {
        ComponentFactory.Register("Stub2", () => new StubComponent());
        Assert.IsTrue(ComponentFactory.IsRegistered("Stub2"));
        Assert.IsFalse(ComponentFactory.IsRegistered("Unknown"));
    }
}
