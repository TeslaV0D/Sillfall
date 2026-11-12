using System.Numerics;
using Sillfall.Core.Simulation;

namespace Sillfall.Core.Tests.Simulation;

public class CursorMotionTests
{
    [Fact]
    public void Update_FirstSample_IsAtRest()
    {
        var motion = new CursorMotion();

        motion.Update(new Vector2(100, 100), 1.0);

        motion.Velocity.Should().Be(Vector2.Zero);
        motion.Position.Should().Be(new Vector2(100, 100));
    }

    [Fact]
    public void Update_SteadyMovement_ConvergesToTheTrueVelocity()
    {
        var motion = new CursorMotion();
        for (int i = 0; i <= 30; i++)
        {
            motion.Update(new Vector2(i * 20, 0), i / 60.0);
        }

        motion.Velocity.X.Should().BeApproximately(1200f, 5f);
        motion.Velocity.Y.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void Update_OneJitteryFrame_DoesNotMakeAFullGust()
    {
        var motion = new CursorMotion();
        motion.Update(Vector2.Zero, 0);
        motion.Update(new Vector2(0, 0), 1 / 60.0);

        motion.Update(new Vector2(20, 0), 2 / 60.0);

        motion.Velocity.X.Should().BeLessThan(1200f * 0.4f);
    }

    [Fact]
    public void Update_AfterAPause_StartsFromRest()
    {
        var motion = new CursorMotion();
        motion.Update(Vector2.Zero, 0);
        motion.Update(new Vector2(30, 0), 1 / 60.0);

        motion.Update(new Vector2(500, 0), 2.0);

        motion.Velocity.Should().Be(Vector2.Zero);
    }

    [Fact]
    public void Update_AJumpFasterThanAnyHand_IsATeleportNotAGust()
    {
        var motion = new CursorMotion();
        motion.Update(new Vector2(-10_000, -10_000), 0);

        motion.Update(new Vector2(100, 100), 1 / 60.0);

        motion.Velocity.Should().Be(Vector2.Zero);
        motion.Position.Should().Be(new Vector2(100, 100));
    }

    [Fact]
    public void Update_Stopping_DecaysTheVelocity()
    {
        var motion = new CursorMotion();
        for (int i = 0; i <= 20; i++)
        {
            motion.Update(new Vector2(i * 20, 0), i / 60.0);
        }

        for (int i = 21; i <= 40; i++)
        {
            motion.Update(new Vector2(400, 0), i / 60.0);
        }

        motion.Velocity.Length().Should().BeLessThan(10f);
    }
}
