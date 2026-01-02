
# Lingarr on Steroids - AI Agent Documentation

When proposing a plan, do not assume it is the full correct plan. Always ask for feedback and iterate. You should not assume you have all the information. You should dive into code and understand evvery single aspect of what you are changing and what impact it will have on all other components and or architecture. Ask the user for clarification on things you are unsure of. DO NOT blindly assume anything, everything should be code backed, any claims you present to the user, they need to be vetted by the real code. If some implementation requires internet research, do it.
If you need more debugging information, tell the user or try to get it yourself if possible. Final implementation plans should pass every lint, type build or whatever else there is on top of tests. 
And how does this impact the rest of the codebase? Is thiss really the fix to do or will it break more things than fix? How will this fix affect all other files and components? How exaactly will this resolve the issue? Won't this cause more issues? You need to ask yourself these questions.
When you encounter a persistent error that requires multiple attempts to fix, you MUST generate a 'Lesson Learned'.
Analyze why the first attempt failed.
Abstract the specific error into a general rule.
Update GEMINI.md "lessons learned" section with what you found out and what you can do to improve. Ensure these don't appear in repetition, these lessons should be thhe guide on how to proceed and should serve as a knowledge base for future agents to also reflect and look and act upon in the codebase. Update the "Lessons learned: " with the lessons that will enhance any future agents in coding tasks. It might also appear that the original repo doesn't contain THIS specific gemini.md file, if it doesn't, create it and copy all of these instructions and paste it there. Word for word. 


"Lessons learned:

- **Constructor Dependency Inflation**: Adding a new dependency to a core service (like `TranslationRequestService`) will break all unit tests that manually instantiate that service. Always grep for usages of `new [ServiceName]` in the `*.Tests` project after modifying a constructor to catch and fix broken tests early.
- **Nullability and XML Warnings**: CI environments often treat warnings as errors or run stricter analysis. Proactively fixing `CS8604` (possible null reference) and XML comment mismatches in the files you modify prevented downstream build failures in this task.
- **Vue Type Checking**: Standard `npm run build` behavior can be inconsistent across environments. Use `npx vue-tsc --noEmit` for a reliable, standalone type-check of the frontend during validation.
- **Repair Phase Resilience**: The translation repair phase is naturally prone to handling 'hard-to-translate' lines that may cause LLM output issues like JSON truncation. Repair logic must always implement the same chunking and fallback splitting protection as the main translation loop to avoid cascading job failures when the repair batch itself exceeds provider limits.
"
