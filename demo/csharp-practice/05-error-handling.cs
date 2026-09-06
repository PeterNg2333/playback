// Error handling 練習：分清「可預期的壞輸入」同「真正意外」
Console.WriteLine(TryDescribe("5"));
Console.WriteLine(TryDescribe("-5"));
Console.WriteLine(TryDescribe("abc"));

static string TryDescribe(string input)
{
    try
    {
        int value = ParsePositiveInterval(input);
        return $"OK: interval = {value}";
    }
    catch (ArgumentOutOfRangeException exception)
    {
        return $"REJECTED (out of range): {exception.ParamName}";
    }
    catch (FormatException exception)
    {
        return $"REJECTED (not a number): {exception.Message}";
    }
}

static int ParsePositiveInterval(string input)
{
    if (!int.TryParse(input, out int value))
    {
        throw new FormatException("Interval must be a whole number.");
    }

    return value > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(input), "Interval must be positive.");
}
