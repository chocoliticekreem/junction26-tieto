import type { Metadata } from "next";
import { IBM_Plex_Mono, Instrument_Sans } from "next/font/google";
import { ThemeProvider } from "next-themes";
import "./globals.css";
import { PageEnter } from "@/components/motion";
import { SiteHeader } from "@/components/site-header";
import { currentHuman } from "@/lib/human";
import { isEphemeral } from "@/lib/demo-state";

const sans = Instrument_Sans({ subsets: ["latin"], variable: "--font-instrument", display: "swap" });
const mono = IBM_Plex_Mono({ subsets: ["latin"], weight: ["400", "500", "600", "700"], variable: "--font-plex-mono", display: "swap" });

export const metadata: Metadata = {
  title: "PullReceipts",
  description: "Auditable AI code review: every finding has a receipt, a named human decides.",
};

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const human = await currentHuman();
  return (
    <html lang="en" suppressHydrationWarning className={`${sans.variable} ${mono.variable}`}>
      <body className="min-h-screen bg-background text-foreground antialiased">
        <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange>
          <SiteHeader human={human} />
          <main className="mx-auto w-full max-w-6xl overflow-x-clip px-4 py-6 sm:px-6 sm:py-8">
            <PageEnter>{children}</PageEnter>
          </main>
          {isEphemeral() && (
            <footer className="mx-auto w-full max-w-6xl px-4 pb-6 text-xs text-muted-foreground sm:px-6" data-testid="ephemeral-note">
              Public demo: your clicks are kept in a cookie in your browser only; nothing is saved on the server.{" "}
              <a href="/api/demo-reset" className="underline underline-offset-2 hover:text-foreground" data-testid="demo-reset">
                Reset my demo clicks
              </a>
            </footer>
          )}
        </ThemeProvider>
      </body>
    </html>
  );
}
