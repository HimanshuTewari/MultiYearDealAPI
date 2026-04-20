import * as React from "react";

export default function usePagination({
  items,
  defaultPageSize,
}: {
  items: Array<any>;
  defaultPageSize?: number;
}) {
  const [pageSize, setPageSize] = React.useState<number>(defaultPageSize || 20);
  const [currentPage, setCurrentPage] = React.useState<number>(1);

  const totalItems = React.useMemo(() => {
    try {
      return items.length;
    } catch (error) {
      console.log(error);
      return 1;
    }
  }, [items]);

  const totalPages = React.useMemo(() => {
    try {
      return Math.ceil(totalItems / pageSize);
    } catch (error) {
      console.log(error);
      return 1;
    }
  }, [totalItems, pageSize]);

  const startIndex = React.useMemo(() => {
    try {
      return (currentPage - 1) * pageSize;
    } catch (error) {
      console.log(error);
      return 1;
    }
  }, [currentPage, pageSize])

  const endIndex = React.useMemo(() => {
    try {
      return Math.min(currentPage * pageSize, totalItems);
    } catch (error) {
      console.log(error);
      return 1;
    }
  }, [currentPage, pageSize])

  const paginatedItems = React.useMemo(() => {
    try {
      return items.slice(startIndex, endIndex);
    } catch (error) {
      console.log(error);
      return items;
    }
  }, [items, startIndex, endIndex]);

  const disableNext = React.useMemo(() => {
    try {
      return currentPage >= totalPages;
    } catch (error) {
      console.log(error);
      return false;
    }
  }, [currentPage, totalPages])

  const disablePrevious = React.useMemo(() => {
    try {
      return currentPage <= 1;
    } catch (error) {
      console.log(error);
      return false;
    }
  }, [currentPage]);

  const disableFirstPage = React.useMemo(() => {
    try {
      return currentPage <= 1
    } catch (error) {
      return false;
    }
  }, [currentPage]);

  const disableLastPage = React.useMemo(() => {
    try {
      return currentPage >= totalPages
    } catch (error) {
      console.log(error);
      return false;
    }
  }, [currentPage, totalPages])

  const paginationMetadata = React.useMemo(() => {
    try {
      return `Showing ${startIndex + 1}-${endIndex} of ${totalItems}`
    } catch (error) {
      console.log(error);
      return ""
    }
  }, [totalItems, startIndex, endIndex])


  function changePageSize(page: number) {
    if (page >= 1 || page < totalItems) {
      setCurrentPage(1);
      setPageSize(page)
    }
  }

  function nextPage() {
    if (currentPage < totalPages) {
      setCurrentPage((currentPage) => currentPage + 1);
    }
  }

  function previousPage() {
    if (currentPage > 1) {
      setCurrentPage((currentPage) => currentPage - 1);
    }
  }

  function firstPage() {
    setCurrentPage(1)
  }

  function lastPage() {
    setCurrentPage(totalPages)
  }

  React.useEffect(() => {
    setCurrentPage(1);
    setPageSize(defaultPageSize || 20)
  }, [items])

  return {
    paginatedItems,
    paginationMetadata,
    currentPage,
    pageSize,
    disabled: {
      next: disableNext,
      previous: disablePrevious,
      first: disableFirstPage,
      last: disableLastPage
    },
    nextPage,
    previousPage,
    firstPage,
    lastPage,
    setPageSize,
    changePageSize
  };
}
