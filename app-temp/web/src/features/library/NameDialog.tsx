import { TextInputDialog } from "../../Component/Dialog/TextInputDialog";
import { usePlaybackStore } from "../../pages/store";
import { closeNameDialog, submitName } from "./useLibrary";

// Asks for the name of a new session or group, or a group's new name.
export function NameDialog() {
  const request = usePlaybackStore((state) => state.textDialog);
  const error = usePlaybackStore((state) => state.textDialogError);
  const saving = usePlaybackStore((state) => state.busy === "dialog");
  return (
    <TextInputDialog
      open={request !== null}
      title={
        request?.kind === "session"
          ? "New session"
          : request?.kind === "group"
            ? "New group"
            : "Rename group"
      }
      label={request?.kind === "session" ? "Session title" : "Group name"}
      initialValue={request?.kind === "rename-group" ? request.current : ""}
      maxLength={request?.kind === "session" ? 120 : 80}
      busy={saving}
      error={error}
      onCancel={closeNameDialog}
      onSubmit={submitName}
    />
  );
}
