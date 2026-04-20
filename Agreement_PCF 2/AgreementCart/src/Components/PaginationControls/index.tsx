import * as React from "react";
import "./index.css";
import { Button, Select } from "antd";
import ArrowLeftIcon from "@mui/icons-material/ArrowLeft";
import ArrowRightIcon from "@mui/icons-material/ArrowRight";
import KeyboardDoubleArrowLeftIcon from "@mui/icons-material/KeyboardDoubleArrowLeft";
import KeyboardDoubleArrowRightIcon from "@mui/icons-material/KeyboardDoubleArrowRight";

export default function PaginationControls({
  currentPage,
  pageSize,
  paginationMetadata,
  disabled,
  nextPage,
  previousPage,
  firstPage,
  lastPage,
  changePageSize,
}: {
  currentPage: number;
  pageSize: number;
  paginationMetadata: string;
  disabled: {
    next: boolean;
    previous: boolean;
    first: boolean;
    last: boolean;
  };
  nextPage: () => void;
  previousPage: () => void;
  firstPage: () => void;
  lastPage: () => void;
  changePageSize: (page: number) => void;
}) {

  const pageOptions = React.useMemo(() => {
    const optionsSet = new Set([10, 20, 50, 100, 150, pageSize]);
    const options = Array.from(optionsSet).sort((a, b) => a - b);
    return options.map((o) => ({ value: `${o}`, label: `${o}` }));
  }, [pageSize]);

  return (
    <div className="pagination-controls">
      <div className="pagination-buttons">
        <Button
          onClick={firstPage}
          disabled={disabled.first}
          icon={<KeyboardDoubleArrowLeftIcon sx={{ color: "var(--text-3)" }} />}
        />
        <Button
          onClick={previousPage}
          disabled={disabled.previous}
          icon={<ArrowLeftIcon sx={{ color: "var(--text-3)" }} />}
        />
        <Button>{currentPage}</Button>
        <Button
          onClick={nextPage}
          disabled={disabled.next}
          icon={<ArrowRightIcon sx={{ color: "var(--text-3)" }} />}
        />
        <Button
          onClick={lastPage}
          disabled={disabled.last}
          icon={
            <KeyboardDoubleArrowRightIcon sx={{ color: "var(--text-3)" }} />
          }
        />
      </div>
      <div className="pagination-metadata">
        <p>{paginationMetadata}</p>
        <span className="page-size">Page Size:
          <Select onChange={(v) => changePageSize(parseInt(v))} value={`${pageSize}`}>
            {pageOptions.map((page) => (
              <Select.Option key={page.value} value={page.value}>
                {page.label}
              </Select.Option>
            ))}
          </Select>

        </span>
      </div>
    </div>
  );
}
