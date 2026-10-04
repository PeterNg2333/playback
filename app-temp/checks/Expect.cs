// The assertions every check uses. A failed expectation throws CheckFailed, never InvalidOperationException,
// because many checks expect the product to reject bad input with InvalidOperationException.
static class Expect
{
    public static void That(bool condition, string expectation)
    {
        if (!condition) throw new CheckFailed(expectation);
    }

    public static void Rejects(Action work, string expectation)
    {
        try { work(); }
        catch (InvalidOperationException) { return; }
        throw new CheckFailed(expectation);
    }

    public static async Task RejectsAsync(Func<Task> work, string expectation)
    {
        try { await work(); }
        catch (InvalidOperationException) { return; }
        throw new CheckFailed(expectation);
    }
}

sealed class CheckFailed(string expectation) : Exception(expectation);
