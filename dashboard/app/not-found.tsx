import Link from "next/link";
import { Slip } from "@/components/mascot";

export default function NotFound() {
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 py-16 text-center">
      <Slip pose="empty" size={120} label="Slip with nothing to show" />
      <h1 className="text-xl font-semibold tracking-tight">Nothing here</h1>
      <p className="text-sm text-muted-foreground">No run was recorded under this address. A change only exists once the pipeline has reviewed it.</p>
      <Link href="/" className="text-sm text-primary underline-offset-4 hover:underline">
        Back to your queue
      </Link>
    </div>
  );
}
