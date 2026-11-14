import Link from "next/link";
import { ReceiptText } from "lucide-react";
import { NamePicker } from "./name-picker";
import { NavLinks } from "./nav-links";
import { ThemeToggle } from "./theme-toggle";

export function SiteHeader({ human }: { human: string | null }) {
  return (
    <header className="sticky top-0 z-20 border-b bg-background/85 backdrop-blur supports-[backdrop-filter]:bg-background/70">
      <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-2.5 sm:px-6">
        <Link href="/" className="flex items-center gap-2 rounded-sm">
          <span className="flex size-6 items-center justify-center rounded-md bg-primary text-primary-foreground">
            <ReceiptText className="size-3.5" />
          </span>
          <span className="text-sm font-semibold tracking-tight">Receipts</span>
          <span className="hidden text-xs text-muted-foreground md:inline">auditable AI code review</span>
        </Link>
        <NavLinks />
        <div className="ml-auto flex items-center gap-1.5">
          <NamePicker initial={human} />
          <ThemeToggle />
        </div>
      </div>
    </header>
  );
}
