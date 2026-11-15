# Recovery Center — reproduced missing-folder warning

Code fix56f6268. On the prior code, stronger absence checks produced3failed/9passed/0skipped; after the narrowly scoped FileNotFoundException catch12passed. Two cases are new, one prior case is strengthened. General IOException/UnauthorizedAccessException warnings and reparse guards remain.

The separate actual Linux chmod000 probe confirmed denied access still appears unavailable. No Windows ACL denial claim. Final complete Linux core suite514passed/0failed/0skipped; subsets are not added to that total. Windows native/candidate results are tracked separately in the session report.

Raw outputs: [red](recovery-missing-red.txt), [green](recovery-missing-green.txt), [actual access denial](recovery-access-probe.txt). These contain owned synthetic test paths only.
