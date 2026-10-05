// The top line of a small floating panel: its title, and a × that closes it.
export function PopupHeader({
  title,
  closeLabel,
  onClose,
}: {
  title: string;
  closeLabel: string;
  onClose: () => void;
}) {
  return (
    <div className="flex items-start justify-between gap-2.5">
      <strong className="text-[14px]">{title}</strong>
      <button
        type="button"
        className="cursor-pointer px-1.5 py-px text-[19px] text-muted"
        aria-label={closeLabel}
        onClick={onClose}
      >
        ×
      </button>
    </div>
  );
}
