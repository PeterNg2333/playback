// async/await 練習：模擬等待 ASR 服務回應時，不卡住程式
Console.WriteLine("Sending transcript chunk to ASR (simulated)...");
string normalized = await NormalizeTranscriptAsync("  cap theorem intro  ");
Console.WriteLine($"normalized = {normalized}");

static async Task<string> NormalizeTranscriptAsync(string text)
{
    await Task.Delay(300); // 模擬等待 ASR / network 回應
    return text.Trim().ToUpperInvariant();
}
