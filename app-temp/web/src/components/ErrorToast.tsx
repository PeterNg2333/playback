export function ErrorToast({
  message,
  onDismiss,
}: {
  message: string;
  onDismiss: () => void;
}) {
  return (
    <div className="global-error" role="alert" data-testid="error-toast">
      {message}
      <button aria-label="Dismiss error" onClick={onDismiss}>
        ×
      </button>
    </div>
  );
}
