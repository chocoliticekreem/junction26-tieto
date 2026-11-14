import type { Metadata } from "next";
import "./globals.css";
import { SiteHeader } from "@/components/site-header";
import { currentHuman } from "@/lib/human";

export const metadata: Metadata = {
  title: "Receipts",
  description: "Auditable AI code review: every finding has a receipt, a named human decides.",
};

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const human = await currentHuman();
  return (
    <html lang="en">
      <body className="min-h-screen bg-background text-foreground antialiased">
        <SiteHeader human={human} />
        <main className="mx-auto w-full max-w-6xl px-4 py-6 sm:px-6 sm:py-8">{children}</main>
      </body>
    </html>
  );
}
