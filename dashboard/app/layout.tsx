import type { Metadata } from "next";
import { IBM_Plex_Mono, Instrument_Sans } from "next/font/google";
import { ThemeProvider } from "next-themes";
import "./globals.css";
import { PageEnter } from "@/components/motion";
import { SiteHeader } from "@/components/site-header";
import { currentHuman } from "@/lib/human";

const sans = Instrument_Sans({ subsets: ["latin"], variable: "--font-instrument", display: "swap" });
const mono = IBM_Plex_Mono({ subsets: ["latin"], weight: ["400", "500", "600", "700"], variable: "--font-plex-mono", display: "swap" });

export const metadata: Metadata = {
  title: "Receipts",
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
        </ThemeProvider>
      </body>
    </html>
  );
}
