import type { ComponentPropsWithoutRef } from "react";

export function Panel({
  className,
  children,
  ...props
}: ComponentPropsWithoutRef<"section"> & { className: string }) {
  return (
    <section className={`panel ${className}`} {...props}>
      {children}
    </section>
  );
}
