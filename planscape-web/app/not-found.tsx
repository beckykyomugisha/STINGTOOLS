import Link from 'next/link';

export default function NotFound() {
  return (
    <div className="grid min-h-screen place-items-center bg-slate-50 p-6">
      <div className="max-w-md rounded-lg border border-slate-200 bg-white p-6 text-center">
        <h1 className="text-lg font-semibold">Page not found</h1>
        <p className="mt-2 text-sm text-slate-500">That page doesn’t exist or you don’t have access.</p>
        <Link
          href="/projects"
          className="mt-4 inline-block rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
        >
          Back to projects
        </Link>
      </div>
    </div>
  );
}
