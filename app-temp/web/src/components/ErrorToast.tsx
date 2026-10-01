// The page's last error, pinned bottom-left above the player until dismissed.
export function ErrorToast({
  message,
  onDismiss,
}: {
  message: string;
  onDismiss: () => void;
}) {
  return (
    <div
      className="fixed bottom-22.5 left-4 z-30 max-w-120 rounded-[9px] border border-[#efb8c9] bg-[#fff2f6] px-3 py-2.5 text-[12px] text-[#873859] shadow-[0_4px_22px_#26284822]"
      role="alert"
      data-testid="error-toast"
    >
      {message}
      <button
        className="float-right ml-3 px-1.5 py-px"
        aria-label="Dismiss error"
        onClick={onDismiss}
      >
        ×
      </button>
    </div>
  );
}
