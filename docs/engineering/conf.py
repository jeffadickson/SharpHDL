"""Sphinx configuration copied by the managed documentation builder."""
project = "SharpHDL engineering reference"
copyright = "2026, SharpHDL contributors"
extensions = ["myst_parser", "sphinxcontrib.mermaid"]
source_suffix = {".md": "markdown", ".rst": "restructuredtext"}
root_doc = "index"
html_theme = "alabaster"
exclude_patterns = ["_build"]
myst_fence_as_directive = ["mermaid"]
# Canonical pages retain their repository-relative links. The builder packages the
# corresponding tracked raw files; navigation/API links use Sphinx's own toctree.
myst_all_links_external = True
nitpicky = True
mermaid_version = "11.12.0"
# The build driver appends only validated, absolute paths for packaged raw files.
