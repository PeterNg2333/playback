export function Icon({ name }: { name: string }) {
  const path =
    name === "record" ? (
      <circle cx="12" cy="12" r="6" fill="currentColor" stroke="none" />
    ) : name === "pause" ? (
      <>
        <path d="M8 5v14M16 5v14" />
      </>
    ) : name === "stop" ? (
      <rect
        x="6"
        y="6"
        width="12"
        height="12"
        rx="2"
        fill="currentColor"
        stroke="none"
      />
    ) : name === "settings" ? (
      <>
        <circle cx="12" cy="12" r="3" />
        <path d="M12 2v3m0 14v3M2 12h3m14 0h3M4.9 4.9 7 7m10 10 2.1 2.1M19.1 4.9 17 7M7 17l-2.1 2.1" />
      </>
    ) : name === "menu" ? (
      <path d="M4 7h16M4 12h16M4 17h16" />
    ) : name === "wave" ? (
      <path d="M3 12c2.2 0 2.2-5 4.5-5S9.8 17 12 17s2.2-10 4.5-10S18.8 12 21 12" />
    ) : name === "loop" ? (
      <>
        <path d="M19 7a8 8 0 1 0 1 8M19 3v4h-4" />
        <path d="m10 8 6 4-6 4" />
      </>
    ) : (
      <path d="M3 12h3l2-5 3 10 2-7 2 3h6" />
    );
  return (
    <svg
      viewBox="0 0 24 24"
      width="22"
      height="22"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {path}
    </svg>
  );
}
