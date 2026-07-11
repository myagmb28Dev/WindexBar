# WindexBar v1.4 Reddit Update Post Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a confident `r/codex` follow-up announcing that WindexBar v1.4 delivered the community-requested reset-credit expiration feature and added activity-aware auto-show.

**Architecture:** Use the signed-in in-app browser session to create one Reddit text post. Verify every technical claim against the v1.4 release and inspect the published page after submission.

**Tech Stack:** Reddit web UI, Codex in-app Browser, GitHub Releases

## Global Constraints

- Community: `r/codex`
- Flair: `Showcase`
- Do not quote or argue with skeptical comments.
- State that expiration timestamps come directly from Codex app-server and are never estimated when missing.
- State that Codex CLI remains required.
- Submit only one post and do not modify account or community settings.

---

### Task 1: Prepare and verify the final copy

**Files:**
- Reference: `docs/superpowers/specs/2026-07-11-reddit-v1-4-update-post-design.md`
- Reference: `README.md`

**Interfaces:**
- Consumes: verified v1.4 feature facts and public URLs
- Produces: final Reddit title and Markdown body

- [ ] **Step 1: Use this title**

```text
You asked for reset credit expiration tracking — it’s now in WindexBar v1.4
```

- [ ] **Step 2: Use this body**

```markdown
A little over a week ago, I shared WindexBar here — a small Windows tray app for keeping an eye on Codex usage.

The most requested feature was clear: show when each banked reset credit actually expires.

That is now available in WindexBar v1.4.

What changed:

- Exact reset-credit expiration dates and times, read directly from Codex app-server
- Credits with no expiration data are shown as unavailable instead of being estimated
- Optional auto-show while ChatGPT Desktop or a terminal Codex session is active
- The same compact Windows tray workflow, without needing to keep the Codex usage page open

You still need the Codex CLI installed, and updating it to the latest version is recommended because the expiration details depend on the data exposed by app-server.

Thank you to everyone who gave feedback on the first post. You pointed out the biggest missing piece, and it genuinely made WindexBar better.

WindexBar v1.4:
https://github.com/myagmb28Dev/WindexBar/releases/tag/v1.4

GitHub:
https://github.com/myagmb28Dev/WindexBar

Original post:
https://www.reddit.com/r/codex/comments/1ujle47/i_made_a_tiny_windows_tray_app_to_keep_an_eye_on/
```

- [ ] **Step 3: Verify the release URL and claims**

Open `https://github.com/myagmb28Dev/WindexBar/releases/tag/v1.4` and confirm that it lists exact reset-credit expiration details and ChatGPT Desktop auto-show recognition.

Expected: both changes are present in the public v1.4 release notes.

### Task 2: Publish through Reddit

**Files:**
- None

**Interfaces:**
- Consumes: exact title and body from Task 1
- Produces: one public `r/codex` post

- [ ] **Step 1: Open the Reddit submission page**

Open `https://www.reddit.com/r/codex/submit` in the in-app browser.

Expected: the signed-in Reddit post composer for `r/codex` is visible.

- [ ] **Step 2: Enter the post**

Choose a text post, enter the exact Task 1 title and body, and select the `Showcase` flair.

Expected: the preview contains the three correct links and no hostile-comment quotations.

- [ ] **Step 3: Submit exactly once**

Click the Reddit post button once.

Expected: Reddit navigates to the new public post page.

- [ ] **Step 4: Verify the published result**

Confirm the title, `Showcase` flair, body bullets, release link, repository link, and original-post link on the published page.

Expected: all elements are visible and the post is in `r/codex`.
