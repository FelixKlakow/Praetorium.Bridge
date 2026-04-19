# Code Review Request

You are reviewing changes on branch `{{BRANCH}}` against the base branch.

{{#if FOCUS_AREAS}}
## Focus Areas
Pay special attention to:
{{#each FOCUS_AREAS}}
- {{.}}
{{/each}}
{{/if}}

Review type: {{REVIEW_TYPE}}

## Instructions

1. Use your filesystem tools to read the relevant source files
2. Check for bugs, security vulnerabilities, and code quality issues
3. Evaluate naming conventions and code organization
4. Look for potential performance problems
5. Verify error handling is adequate

When your review is complete, call `respond` with your findings. Structure your response with:
- **Summary**: Overall assessment
- **Issues Found**: List of specific issues with file paths and line numbers
- **Suggestions**: Improvement recommendations
- **Verdict**: approve / request-changes / comment
