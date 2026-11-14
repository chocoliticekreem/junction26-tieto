"""`uv run receipts <command>`. Each command lives in its own module; this file only dispatches."""

import argparse
import sys


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(prog="receipts", description="Auditable AI code review")
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("setup", help="clone mock repo, fetch branches, build base once")

    r = sub.add_parser("review", help="review one change end to end")
    r.add_argument("--change", required=True, help="branch name in mock-repo, e.g. ju26-issue-2283")
    r.add_argument("--req", required=True, help="requirement id, e.g. REQ-2283")
    r.add_argument("--base", default=None, help="base sha (default: config.BASE_SHA)")
    r.add_argument("--router", default="auto", choices=["auto", "rule", "llm", "jev", "none"])
    r.add_argument("--skip-mutation", action="store_true")
    r.add_argument("--no-ab", action="store_true",
                   help="record A/B readings but do not build or review the A/B branches")
    r.add_argument("--req-tests-fixture", action="store_true",
                   help="also run fixtures/requirement-tests/<REQ>.cs as requirement tests")

    s = sub.add_parser("review-sql", help="review one SQL migration change in a throwaway Postgres")
    s.add_argument("--repo", required=True, help="bare git repo (examples/make_sql_repo.sh)")
    s.add_argument("--change", required=True, help="branch name, e.g. sql-good")
    s.add_argument("--req", required=True, help="requirement id, e.g. REQ-SQL-1")
    s.add_argument("--base", default="main", help="base ref (default: main)")

    b = sub.add_parser("bench", help="mock-clone bench")
    bsub = b.add_subparsers(dest="bench_cmd", required=True)
    bsub.add_parser("generate", help="apply plants, create mock/<issue>/<variant> branches")
    br = bsub.add_parser("run", help="review every bench variant")
    br.add_argument("--warm", action="store_true", help="keep ledger answers from the cold run")
    br.add_argument("--only", default=None, help="substring filter on manifest ids")
    br.add_argument("--jobs", type=int, default=3, help="cases reviewed in parallel (processes)")

    sub.add_parser("metrics", help="aggregate runs/ into metrics/ T1-T7")
    sub.add_parser("bootstrap-context", help="build context/REVIEW_CONTEXT.md draft from the repo")

    args = p.parse_args(argv)

    if args.cmd == "setup":
        from .setup import run_setup

        return run_setup()
    if args.cmd == "review":
        from .review import run_review

        return run_review(args.change, args.req, base=args.base, router=args.router,
                          skip_mutation=args.skip_mutation,
                          req_tests_fixture=args.req_tests_fixture, no_ab=args.no_ab)
    if args.cmd == "review-sql":
        from pathlib import Path

        from .review_sql import run_review_sql

        return run_review_sql(Path(args.repo), args.change, args.req, base=args.base)
    if args.cmd == "bench":
        from . import bench

        if args.bench_cmd == "generate":
            return bench.generate()
        return bench.run(warm=args.warm, only=args.only, jobs=args.jobs)
    if args.cmd == "metrics":
        from .metrics import run_metrics

        return run_metrics()
    if args.cmd == "bootstrap-context":
        from .context import bootstrap

        return bootstrap()
    return 1


if __name__ == "__main__":
    sys.exit(main())
