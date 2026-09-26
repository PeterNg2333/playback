import { useEffect, useRef, useState } from "react";

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
    <dialog
      ref={dialog}
      className="text-dialog"
      onCancel={(event) => {
        event.preventDefault();
        if (!busy) onCancel();
      }}
      aria-labelledby="text-dialog-title"
    >
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void onSubmit(value);
        }}
      >
        <h2 id="text-dialog-title">{title}</h2>
        <label htmlFor="text-dialog-input">{label}</label>
        <input
          ref={input}
          id="text-dialog-input"
          value={value}
          maxLength={maxLength}
          required
          onChange={(event) => setValue(event.target.value)}
          aria-describedby={error ? "text-dialog-error" : undefined}
        />
        {error && (
          <p id="text-dialog-error" role="alert">
            {error}
          </p>
        )}
        <div className="text-dialog-actions">
          <button type="button" onClick={onCancel} disabled={busy}>
            Cancel
          </button>
          <button type="submit" disabled={busy}>
            {busy ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </dialog>
  );
}
