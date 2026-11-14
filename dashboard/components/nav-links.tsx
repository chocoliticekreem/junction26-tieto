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
    <nav className="order-last flex w-full gap-1 overflow-x-auto text-sm sm:order-none sm:w-auto">
      {links.map((l) => {
        const active = l.href === "/" ? pathname === "/" || pathname.startsWith("/change") || pathname.startsWith("/decision") : pathname.startsWith(l.href);
        return (
          <Link
            key={l.href}
            href={l.href}
            className={cn(
              "rounded-md px-2.5 py-1 whitespace-nowrap text-muted-foreground hover:bg-muted hover:text-foreground",
              active && "bg-muted font-medium text-foreground",
            )}
          >
            {l.label}
          </Link>
        );
      })}
    </nav>
  );
}
