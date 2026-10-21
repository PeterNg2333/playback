namespace Playback.Application.Recording;
public interface IRecordingControl
{
    CaptureStatus Status();
    Task<CaptureStatus> Start(string sessionId, string sourceMode = "microphone");
    Task<CaptureStatus> Stop();
    Task<CaptureStatus> Pause();
    Task<CaptureStatus> Resume();
    Task DeleteSession(string sessionId);
}
public interface IRecordingAccess
{
    Task RequireSessionOwner(string id);
    Task RequestNoteFlush(string id);
}
public sealed class RecordingCommands(IRecordingControl control, IRecordingAccess access)
{
    public async Task<CaptureStatus> Status() {var status = control.Status(); if (status.SessionId is { } id) await access.RequireSessionOwner(id); return status;}
    public async Task<CaptureStatus> Start(string id, string mode) {await access.RequireSessionOwner(id); return await control.Start(id, mode);}
    public async Task<CaptureStatus> Stop() {var id = control.Status().SessionId; if (id is not null) await access.RequireSessionOwner(id); var status = await control.Stop(); if (id is not null) await access.RequestNoteFlush(id); return status;}
    public async Task<CaptureStatus> Pause() {await Status(); return await control.Pause();}
    public async Task<CaptureStatus> Resume() {await Status(); return await control.Resume();}
    public Task DeleteSession(string id) => control.DeleteSession(id);
}
