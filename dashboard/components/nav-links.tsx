"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";

const links = [
  { href: "/", label: "Inbox" },
  { href: "/questions", label: "Questions" },
  { href: "/context", label: "Context" },
  { href: "/track-record", label: "Track record" },
];

export function NavLinks() {
  const pathname = usePathname();
  return (
    <nav className="order-last -mb-2.5 flex w-full gap-1 overflow-x-auto text-sm sm:order-none sm:mb-0 sm:w-auto">
      {links.map((l) => {
        const active = l.href === "/" ? pathname === "/" || pathname.startsWith("/change") || pathname.startsWith("/decision") : pathname.startsWith(l.href);
        return (
          <Link
            key={l.href}
            href={l.href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "relative rounded-md px-2.5 py-1.5 whitespace-nowrap text-muted-foreground transition-colors duration-150 hover:bg-muted hover:text-foreground sm:py-1",
              active && "font-medium text-foreground after:absolute after:inset-x-2.5 after:-bottom-1 after:h-0.5 after:rounded-full after:bg-primary sm:after:-bottom-[9px]",
            )}
          >
            {l.label}
          </Link>
        );
      })}
    </nav>
  );
}
