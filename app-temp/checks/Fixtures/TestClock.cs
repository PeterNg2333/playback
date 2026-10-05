// A clock the note checks move by hand, so the 10-second note schedule runs without waiting.
sealed class TestClock : TimeProvider
{
    long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero).AddTicks(ticks);
    public void Advance(double seconds) => ticks += (long)(seconds * TimeSpan.TicksPerSecond);
}
