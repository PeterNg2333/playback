using Playback.Api.Db;

// Synthetic lectures: three hours of 30-second passages, and the widest passage one citation alias can cover.
static class LectureFixtures
{
    // 360 passages; only passage 4 (a spectrogram) and passage 355 (a system call) say anything specific.
    public static List<Transcript> ThreeHours() => Enumerable.Range(0, 360).Select(i => new Transcript
    {
        Id = $"long-{i}",
        StartMs = i * 30_000,
        EndMs = (i + 1) * 30_000,
        Original = i == 4 ? "A spectrogram shows frequency over time." :
            i == 355 ? "The kernel handles a system call." : $"Lecture segment number {i}."
    }).ToList();

    // The same three hours in Chinese; only passage 5 explains the Fourier transform.
    public static List<Transcript> ThreeHoursInChinese() => Enumerable.Range(0, 360).Select(i => new Transcript
    {
        Id = $"zh-{i}",
        StartMs = i * 30_000,
        EndMs = (i + 1) * 30_000,
        Original = i == 5 ? "傅立葉變換把訊號分解成頻率成分，FFT 是快速計算法。" : $"第{i}段的課堂練習。"
    }).ToList();

    // Sixteen 3-second microphone chunks with full-length canonical IDs: one grouped source.
    public static List<Transcript> WidestPassage() => Enumerable.Range(0, 16).Select(index => new Transcript
    {
        Id = new string('a', 32) + "-microphone-63926191107278790-" + new string('b', 64) + index,
        SourceId = "microphone",
        StartMs = index * 3000,
        EndMs = (index + 1) * 3000,
        Original = "A bottleneck limits throughput."
    }).ToList();

    public static SessionView Session(List<Transcript> transcripts, List<Material>? materials = null) =>
        new("synthetic", "Three hours", null, DateTime.UtcNow, "", 0, 0, false, "zh-Hant", materials ?? [], transcripts, [], [], [], null);
}
