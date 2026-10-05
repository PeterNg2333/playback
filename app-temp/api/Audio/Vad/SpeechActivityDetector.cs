using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Playback.Api.Audio.Vad;

// Silero VAD: https://github.com/snakers4/silero-vad (MIT license).
// One detector belongs to one capture source because the ONNX recurrent state is source-specific.
public sealed class SpeechActivityDetector : IDisposable
{
    const int ModelRate = 16_000;
    const int FrameSamples = 512;
    const int ContextSamples = 64;
    readonly InferenceSession session;
    readonly float[] context = new float[ContextSamples];
    readonly float[] state = new float[256];
    readonly float[] frame = new float[FrameSamples];
    int frameCount;
    int inputRate;
    int resamplePhase;
    float resampleSum;
    int resampleCount;
    long lastVoiceAtMs = -1;
    long voicedMs;

    public SpeechActivityDetector()
    {
        var model = Path.Combine(AppContext.BaseDirectory, "Resources", "silero_vad.onnx");
        if (!File.Exists(model)) throw new FileNotFoundException("Silero VAD model is missing", model);
        using var options = new Microsoft.ML.OnnxRuntime.SessionOptions { InterOpNumThreads = 1, IntraOpNumThreads = 1 };
        session = new InferenceSession(model, options);
    }

    public long VoicedMs => voicedMs;
    public long QuietForMs(long nowMs) => lastVoiceAtMs < 0 ? long.MaxValue : Math.Max(0, nowMs - lastVoiceAtMs);
    public bool Speaking(long nowMs) => lastVoiceAtMs >= 0 && nowMs - lastVoiceAtMs <= 350;

    public bool Process(ReadOnlySpan<float> mono, int sampleRate, long nowMs)
    {
        if (sampleRate < ModelRate && sampleRate != 8_000)
            throw new NotSupportedException($"VAD cannot decode {sampleRate} Hz input");
        if (sampleRate != inputRate)
        {
            inputRate = sampleRate;
            resamplePhase = 0;
            resampleSum = 0;
            resampleCount = 0;
            frameCount = 0;
            lastVoiceAtMs = -1;
            voicedMs = 0;
            Array.Clear(context);
            Array.Clear(state);
        }
        var voiceDetected = false;
        foreach (var sample in mono)
        {
            if (sampleRate == 8_000)
            {
                if (PushSample(sample, nowMs)) voiceDetected = true;
                if (PushSample(sample, nowMs)) voiceDetected = true;
                continue;
            }
            resampleSum += sample;
            resampleCount++;
            resamplePhase += ModelRate;
            if (resamplePhase < sampleRate) continue;
            resamplePhase -= sampleRate;
            var reduced = resampleSum / resampleCount;
            resampleSum = 0;
            resampleCount = 0;
            if (PushSample(reduced, nowMs)) voiceDetected = true;
        }
        return voiceDetected;
    }

    bool PushSample(float sample, long nowMs)
    {
        frame[frameCount++] = sample;
        if (frameCount < FrameSamples) return false;
        frameCount = 0;
        if (Infer() < 0.5f) return false;
        if (!Speaking(nowMs)) voicedMs = 0;
        lastVoiceAtMs = nowMs;
        voicedMs += 32;
        return true;
    }

    float Infer()
    {
        var input = new float[ContextSamples + FrameSamples];
        context.CopyTo(input, 0);
        frame.CopyTo(input, ContextSamples);
        frame.AsSpan(FrameSamples - ContextSamples).CopyTo(context);
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, input.Length])),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state, [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { ModelRate }, [1])),
        };
        using var result = session.Run(inputs);
        var next = result.First(value => value.Name == "stateN").AsTensor<float>();
        next.ToArray().CopyTo(state, 0);
        return result.First(value => value.Name == "output").AsTensor<float>()[0, 0];
    }

    public void Dispose() => session.Dispose();
}
