"use client";

import { useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Depth } from "@/lib/types";
import { depthStyle } from "./pills";

interface Node {
  name: string;
  path: string;
  depth?: Depth;
  children: Map<string, Node>;
  files: number;
  depths: Set<Depth>;
}

const ORDER: Depth[] = ["deep", "light", "regeneration"];

function build(routing: Record<string, Depth>): Node {
  const root: Node = { name: "", path: "", children: new Map(), files: 0, depths: new Set() };
  for (const [file, depth] of Object.entries(routing)) {
    const parts = file.split("/");
    let cur = root;
    cur.files += 1;
    cur.depths.add(depth);
    parts.forEach((part, i) => {
      const path = parts.slice(0, i + 1).join("/");
      let next = cur.children.get(part);
      if (!next) {
        next = { name: part, path, children: new Map(), files: 0, depths: new Set() };
        cur.children.set(part, next);
      }
      next.files += 1;
      next.depths.add(depth);
      if (i === parts.length - 1) next.depth = depth;
      cur = next;
    });
  }
  return collapseSingleChildDirs(root);
}

/** a/b/c with one child each renders as one row "a/b/c". */
function collapseSingleChildDirs(node: Node): Node {
  for (const [key, child] of node.children) {
    let c = child;
    while (c.depth === undefined && c.children.size === 1) {
      const only = [...c.children.values()][0];
      if (only.depth !== undefined) break;
      c = { ...only, name: `${c.name}/${only.name}` };
    }
    node.children.set(key, collapseSingleChildDirs(c));
  }
  return node;
}

function sortNodes(nodes: Node[]): Node[] {
  return nodes.sort((a, b) => {
    const ad = a.depth === undefined ? 0 : 1;
    const bd = b.depth === undefined ? 0 : 1;
    if (ad !== bd) return ad - bd;
    return a.name.localeCompare(b.name);
  });
}

function uniformDepth(n: Node): Depth | undefined {
  return n.depths.size === 1 ? [...n.depths][0] : undefined;
}

function Row({ node, level }: { node: Node; level: number }) {
  const uniform = uniformDepth(node);
  const isDir = node.depth === undefined;
  // Directories where every file shares one depth collapse to a single counted row.
  const collapsible = isDir && uniform !== undefined && node.files > 3;
  const [open, setOpen] = useState(!collapsible);
  const pad = { paddingLeft: `${level * 14 + 8}px` };

  if (!isDir) {
    const s = depthStyle[node.depth!];
    return (
      <div className="flex items-center gap-2 py-0.5 pr-2 font-mono text-xs" style={pad}>
        <span className={cn("size-1.5 shrink-0 rounded-full", s.dot)} />
        <span className="truncate">{node.name}</span>
        <span className={cn("ml-auto shrink-0 text-[10px]", s.pill.split(" ").filter((c) => c.startsWith("text-")).join(" "))}>{s.label}</span>
      </div>
    );
  }

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center gap-1.5 py-0.5 pr-2 text-left font-mono text-xs hover:bg-muted/60"
        style={pad}
        aria-expanded={open}
      >
        {open ? <ChevronDown className="size-3 shrink-0 text-muted-foreground" /> : <ChevronRight className="size-3 shrink-0 text-muted-foreground" />}
        <span className="truncate text-foreground/80">{node.name}/</span>
        <span className="ml-auto flex shrink-0 items-center gap-2 text-[10px] text-muted-foreground tabular-nums">
          {node.files} files
          {uniform && (
            <span className="inline-flex items-center gap-1">
              <span className={cn("size-1.5 rounded-full", depthStyle[uniform].dot)} />
              {depthStyle[uniform].label}
            </span>
          )}
        </span>
      </button>
      {open && (
        <div>
          {sortNodes([...node.children.values()]).map((c) => (
            <Row key={c.path} node={c} level={level + 1} />
          ))}
        </div>
      )}
    </div>
  );
}

export function RoutingTree({ routing }: { routing: Record<string, Depth> }) {
  const root = build(routing);
  const counts = ORDER.map((d) => [d, Object.values(routing).filter((v) => v === d).length] as const);
  const total = Object.keys(routing).length;
  if (total === 0) return <p className="text-sm text-muted-foreground">No routing map in this run.</p>;
  return (
    <div className="rounded-md border" data-testid="routing-tree">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b bg-muted/40 px-3 py-2 text-xs">
        <span className="font-mono tabular-nums">{total} files</span>
        {counts.map(([d, n]) => (
          <span key={d} className="inline-flex items-center gap-1.5 text-muted-foreground">
            <span className={cn("size-1.5 rounded-full", depthStyle[d].dot)} />
            <span className="font-mono tabular-nums text-foreground">{n}</span> {depthStyle[d].label}
          </span>
        ))}
      </div>
      <div className="py-1">
        {sortNodes([...root.children.values()]).map((c) => (
          <Row key={c.path} node={c} level={0} />
        ))}
      </div>
    </div>
  );
}
