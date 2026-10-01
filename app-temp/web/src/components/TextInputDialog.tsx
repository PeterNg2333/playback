import { useEffect, useRef, useState } from "react";
import { Dialog, DialogActions, DialogButton } from "./Dialog";

type Props = {
  open: boolean;
  title: string;
  label: string;
  initialValue: string;
  maxLength: number;
  busy: boolean;
  error: string;
  onCancel: () => void;
  onSubmit: (value: string) => void | Promise<void>;
};

export function TextInputDialog({
  open,
  title,
  label,
  initialValue,
  maxLength,
  busy,
  error,
  onCancel,
  onSubmit,
}: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const [value, setValue] = useState(initialValue);

  useEffect(() => {
    setValue(initialValue);
  }, [initialValue, title, open]);
  useEffect(() => {
    if (open && !dialog.current?.open) {
      dialog.current?.showModal();
      input.current?.focus();
    } else if (!open && dialog.current?.open) {
      dialog.current.close();
    }
  }, [open]);

  return (
    <Dialog
      ref={dialog}
      onCancel={(event) => {
        event.preventDefault();
        if (!busy) onCancel();
      }}
      aria-labelledby="text-dialog-title"
    >
      <form
        className="grid gap-3 p-5.5"
        onSubmit={(event) => {
          event.preventDefault();
          void onSubmit(value);
        }}
      >
        <h2 id="text-dialog-title" className="mb-1 text-[17px] font-[750]">
          {title}
        </h2>
        <label htmlFor="text-dialog-input" className="text-[12px] font-bold">
          {label}
        </label>
        <input
          ref={input}
          id="text-dialog-input"
          className="w-full rounded-lg border border-line bg-white px-3 py-2.5 text-ink"
          value={value}
          maxLength={maxLength}
          required
          onChange={(event) => setValue(event.target.value)}
          aria-describedby={error ? "text-dialog-error" : undefined}
        />
        {error && (
          <p
            id="text-dialog-error"
            className="text-[12px] text-[#a13232]"
            role="alert"
          >
            {error}
          </p>
        )}
        <DialogActions>
          <DialogButton type="button" onClick={onCancel} disabled={busy}>
            Cancel
          </DialogButton>
          <DialogButton variant="primary" type="submit" disabled={busy}>
            {busy ? "Saving…" : "Save"}
          </DialogButton>
        </DialogActions>
      </form>
    </Dialog>
  );
}
