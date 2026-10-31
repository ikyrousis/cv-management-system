import React from 'react';

// Windowed page numbers: first, last, and a band around the current page, with a gap marker.
function pageNumbers(page, pageCount, band = 1) {
  const wanted = new Set([1, pageCount, page]);

  for (let offset = 1; offset <= band; offset += 1) {
    wanted.add(page - offset);
    wanted.add(page + offset);
  }

  const pages = [...wanted].filter((n) => n >= 1 && n <= pageCount).sort((a, b) => a - b);

  const rendered = [];
  pages.forEach((n, index) => {
    if (index > 0 && n - pages[index - 1] > 1) {
      rendered.push('gap');
    }
    rendered.push(n);
  });

  return rendered;
}

function Pager({ page, pageSize, total, onPageChange, singular = 'item' }) {
  const pageCount = Math.max(1, Math.ceil(total / pageSize));

  if (total === 0) {
    return null;
  }

  const first = (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, total);

  return (
    <nav
      aria-label={`${singular} pagination`}
      className="d-flex justify-content-between align-items-center flex-wrap gap-2 mt-3"
    >
      <span className="text-muted small">
        Showing {first}-{last} of {total} {singular}
      </span>
      <ul className="pagination pagination-sm mb-0">
        <li className={`page-item ${page <= 1 ? 'disabled' : ''}`}>
          <button
            type="button"
            className="page-link"
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1}
          >
            Previous
          </button>
        </li>

        {pageNumbers(page, pageCount).map((entry, index) =>
          entry === 'gap' ? (
            <li className="page-item disabled" key={`gap-${index}`}>
              <span className="page-link">&hellip;</span>
            </li>
          ) : (
            <li className={`page-item ${entry === page ? 'active' : ''}`} key={entry}>
              <button type="button" className="page-link" onClick={() => onPageChange(entry)}>
                {entry}
              </button>
            </li>
          )
        )}

        <li className={`page-item ${page >= pageCount ? 'disabled' : ''}`}>
          <button
            type="button"
            className="page-link"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= pageCount}
          >
            Next
          </button>
        </li>
      </ul>
    </nav>
  );
}

export default Pager;
