import Link from "next/link";
import { NamePicker } from "./name-picker";
import { NavLinks } from "./nav-links";

export function SiteHeader({ human }: { human: string | null }) {
  return (
    <header className="border-b bg-background">
      <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3 sm:px-6">
        <Link href="/" className="flex items-baseline gap-2">
          <span className="text-sm font-semibold tracking-tight">Receipts</span>
          <span className="hidden text-xs text-muted-foreground sm:inline">auditable AI code review</span>
        </Link>
        <NavLinks />
        <div className="ml-auto">
          <NamePicker initial={human} />
        </div>
      </div>
    </header>
  );
}
