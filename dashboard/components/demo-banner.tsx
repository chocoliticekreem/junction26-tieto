import { RotateCcw } from "lucide-react";

export function DemoBanner() {
  return (
    <div className="border-b bg-primary/[0.07] text-[13px] leading-5" data-testid="demo-banner">
      <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-4 gap-y-1 px-4 py-2 sm:px-6">
        <p className="min-w-0 flex-1">
          <span className="font-semibold">Live demo on real reviews of Tieto&apos;s AI-written changes.</span>{" "}
          <span className="text-muted-foreground">
            Click anything: confirm the context, answer questions, sign off or pick A or B. Here your decisions stay in your browser and never reach the repo.
          </span>
        </p>
        <a
          href="/api/demo-reset"
          className="inline-flex shrink-0 items-center gap-1.5 rounded-full border bg-card px-3 py-1 text-xs font-medium hover:border-primary hover:text-primary"
          data-testid="demo-reset"
        >
          <RotateCcw className="size-3.5" aria-hidden />
          Start over
        </a>
      </div>
    </div>
  );
}
