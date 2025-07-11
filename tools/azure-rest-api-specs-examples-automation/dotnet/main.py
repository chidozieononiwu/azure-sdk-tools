import sys
import os
import glob
import json
import argparse
import logging
import dataclasses
from typing import List
import importlib.util

from models import DotNetExample
from build import DotNetBuild


spec_location = (
    "./directory/examples_dir.py" if os.path.exists("./directory/examples_dir.py") else "../directory/examples_dir.py"
)
spec = importlib.util.spec_from_file_location("examples_dir", spec_location)
examples_dir = importlib.util.module_from_spec(spec)
spec.loader.exec_module(examples_dir)


script_path: str = "."
tmp_path: str
specs_path: str
sdk_package_path: str

original_file_key: str = "// Generated from example definition: "

module_relative_path: str = ""


@dataclasses.dataclass(eq=True, frozen=True)
class Release:
    tag: str
    package: str
    version: str


@dataclasses.dataclass(eq=True)
class DotNetExampleMethodContent:
    example_relative_path: str = None
    content: List[str] = None
    line_start: int = None
    line_end: int = None

    def is_valid(self) -> bool:
        return self.example_relative_path is not None


@dataclasses.dataclass(eq=True)
class AggregatedDotNetExample:
    methods: List[DotNetExampleMethodContent]
    class_opening: List[str] = None


def is_aggregated_dotnet_example(lines: List[str]) -> bool:
    # check metadata to see if the sample file is a candidate for example extraction

    for line in lines:
        if line.strip().startswith(original_file_key):
            return True
    return False


def get_dotnet_example_method(lines: List[str], start: int) -> DotNetExampleMethodContent:
    # extract one example method, start from certain line number

    method_indent = None
    dotnet_example_method = DotNetExampleMethodContent()
    for index in range(len(lines)):
        if index < start:
            continue

        line = lines[index]
        if line.strip().startswith(original_file_key):
            original_file = line.strip()[len(original_file_key) :]
            # begin of method
            dotnet_example_method.example_relative_path = original_file
            dotnet_example_method.line_start = index

            method_indent = len(line) - len(line.lstrip())
        elif method_indent and line.rstrip() == (" " * (method_indent - 4) + "}"):
            # end of method
            dotnet_example_method.line_end = index
            break

    dotnet_example_method.content = lines[dotnet_example_method.line_start : dotnet_example_method.line_end]

    return dotnet_example_method


def get_dotnet_using_statements(lines: List[str]) -> List[str]:
    lines_using_statements = [
        # these are some using statements that every sample program should use.
        "using Azure;\n",
        "using Azure.ResourceManager;\n",
    ]
    for line in lines:
        if line.startswith("using NUnit."):
            # ignore the NUnit namespaces if any
            pass
        elif line.startswith("using "):
            lines_using_statements.append(line)
        elif line.startswith("namespace "):
            # remove the prefix first
            namespace = line[len("namespace ") :].strip()
            # remove the '.Samples' suffix if any
            if namespace.endswith(".Samples"):
                namespace = namespace[: -len(".Samples")]
            lines_using_statements.append(f"using {namespace};\n")
            break
    return deduplicate_list(lines_using_statements)


def deduplicate_list(list: List[str]) -> List[str]:
    seen = set()
    result: List[str] = []
    for item in list:
        if item not in seen:
            seen.add(item)
            result.append(item)
    return result


def break_down_aggregated_dotnet_example(lines: List[str]) -> AggregatedDotNetExample:
    aggregated_dotnet_example = AggregatedDotNetExample([])
    aggregated_dotnet_example.class_opening = get_dotnet_using_statements(lines)
    aggregated_dotnet_example.class_opening.append("\n")

    dotnet_example_method = get_dotnet_example_method(lines, 0)
    while dotnet_example_method.is_valid():
        aggregated_dotnet_example.methods.append(dotnet_example_method)
        dotnet_example_method = get_dotnet_example_method(lines, dotnet_example_method.line_end)
    return aggregated_dotnet_example


def format_dotnet(lines: List[str]) -> List[str]:
    # format example as DotNet code

    base_indent = len(lines[0]) - len(lines[0].lstrip())
    last_good_indent = 0
    new_lines = []
    for line in lines:
        indent = len(line) - len(line.lstrip())
        if indent >= base_indent:
            line = line[base_indent:]
            last_good_indent = indent - base_indent
        else:
            if line.strip():
                line = " " * last_good_indent + line
        new_lines.append(line)

    return new_lines


def process_dotnet_example(filepath: str) -> List[DotNetExample]:
    # process aggregated DotNet sample to examples

    filename = os.path.basename(filepath)
    logging.info(f"Processing DotNet aggregated sample: {filename}")

    with open(filepath, encoding="utf-8") as f:
        lines = f.readlines()

    dotnet_examples = []
    if is_aggregated_dotnet_example(lines):
        aggregated_dotnet_example = break_down_aggregated_dotnet_example(lines)
        for dotnet_example_method in aggregated_dotnet_example.methods:
            if dotnet_example_method.is_valid():
                logging.info(f"Processing DotNet example: {dotnet_example_method.example_relative_path}")

                # re-construct the example class, from example method
                example_lines = aggregated_dotnet_example.class_opening + format_dotnet(dotnet_example_method.content)

                example_filepath = dotnet_example_method.example_relative_path
                example_dir, example_filename = os.path.split(example_filepath)

                try:
                    example_dir = examples_dir.try_find_resource_manager_example(
                        specs_path, sdk_package_path, example_dir, example_filename
                    )
                except NameError:
                    pass

                filename = example_filename.split(".")[0]
                # use the examples-dotnet folder for DotNet example
                md_dir = (
                    (example_dir + "-dotnet")
                    if example_dir.endswith("/examples")
                    else example_dir.replace("/examples/", "/examples-dotnet/")
                )

                dotnet_example = DotNetExample(filename, md_dir, "".join(example_lines))
                dotnet_examples.append(dotnet_example)

    return dotnet_examples


def generate_examples(release: Release, sdk_examples_path: str, dotnet_examples: List[DotNetExample]) -> List[str]:
    # generate code and metadata from DotNet examples

    global module_relative_path

    files = []
    for dotnet_example in dotnet_examples:
        doc_link = f"https://github.com/Azure/azure-sdk-for-net/blob/{release.tag}/{module_relative_path}/README.md"

        files.extend(
            write_code_to_file(
                sdk_examples_path,
                dotnet_example.target_dir,
                dotnet_example.target_filename,
                ".cs",
                dotnet_example.content,
                doc_link,
            )
        )
    return files


def write_code_to_file(
    sdk_examples_path: str, target_dir: str, filename_root: str, filename_ext: str, code_content: str, sdk_url: str
) -> List[str]:
    # write code file and metadata file

    code_filename = filename_root + filename_ext
    metadata_filename = filename_root + ".json"

    metadata_json = {"sdkUrl": sdk_url}

    target_dir_path = os.path.join(sdk_examples_path, target_dir)
    os.makedirs(target_dir_path, exist_ok=True)

    code_file_path = os.path.join(target_dir_path, code_filename)
    with open(code_file_path, "w", encoding="utf-8") as f:
        f.write(code_content)
    logging.info(f"Code written to file: {code_file_path}")

    metadata_file_path = os.path.join(target_dir_path, metadata_filename)
    with open(metadata_file_path, "w", encoding="utf-8") as f:
        json.dump(metadata_json, f)
    logging.info(f"Metadata written to file: {metadata_file_path}")

    return [os.path.join(target_dir, code_filename), os.path.join(target_dir, metadata_filename)]


def create_dotnet_examples(
    release: Release, dotnet_module: str, sdk_examples_path: str, dotnet_examples_paths: List[str]
) -> tuple[bool, List[str]]:
    dotnet_paths = []
    for dotnet_examples_path in dotnet_examples_paths:
        for root, dirs, files in os.walk(dotnet_examples_path):
            for name in files:
                filepath = os.path.join(root, name)
                if os.path.splitext(filepath)[1] == ".cs":
                    dotnet_paths.append(filepath)

    logging.info(f"Processing SDK examples: {release.package}")
    dotnet_examples = []
    for filepath in dotnet_paths:
        dotnet_examples += process_dotnet_example(filepath)

    files = []
    if dotnet_examples:
        dotnet_build = DotNetBuild(tmp_path, dotnet_module.split(",")[0], dotnet_module.split(",")[1], dotnet_examples)
        build_result = dotnet_build.build()

        if build_result.succeeded:
            files = generate_examples(release, sdk_examples_path, dotnet_examples)
        else:
            logging.error("Build failed")

        return build_result.succeeded, files
    else:
        logging.info("SDK examples not found")
        return True, files


def get_module_relative_path(sdk_name: str, sdk_path: str) -> str:
    global module_relative_path
    candidate_sdk_paths = glob.glob(os.path.join(sdk_path, f"sdk/*/{sdk_name}"))
    if len(candidate_sdk_paths) > 0:
        candidate_sdk_paths = [os.path.relpath(p, sdk_path) for p in candidate_sdk_paths]
        logging.info(f"Use first item of {candidate_sdk_paths} for SDK folder")
        module_relative_path = candidate_sdk_paths[0]
    else:
        raise RuntimeError(f"Source folder not found for SDK {sdk_name}")
    return module_relative_path


def find_dotnet_examples_paths(sdk_path: str, module_relative_path_local: str) -> List[str]:
    """
    Find dotnet examples paths in the SDK directory.

    Args:
        sdk_path: Path to the SDK root directory
        module_relative_path_local: Relative path to the module within the SDK

    Returns:
        List of paths where dotnet examples are found
    """
    dotnet_examples_paths: List[str] = []

    # Try primary path first: tests/Generated/Samples
    dotnet_examples_relative_path = os.path.join(module_relative_path_local, "tests", "Generated", "Samples")
    dotnet_examples_path = os.path.join(sdk_path, dotnet_examples_relative_path)

    if os.path.exists(dotnet_examples_path):
        dotnet_examples_paths.append(dotnet_examples_path)
    else:
        # fallback to iterating all directories under tests/*/Generated/Samples
        tests_dir = os.path.join(sdk_path, module_relative_path_local, "tests")
        if os.path.exists(tests_dir):
            for item in os.listdir(tests_dir):
                item_path = os.path.join(tests_dir, item)
                if os.path.isdir(item_path):
                    candidate_path = os.path.join(item_path, "Generated", "Samples")
                    if os.path.exists(candidate_path):
                        dotnet_examples_paths.append(candidate_path)

    return dotnet_examples_paths


def main():
    global script_path
    global tmp_path
    global specs_path
    global sdk_package_path

    logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s", datefmt="%Y-%m-%d %X")

    script_path = os.path.abspath(os.path.dirname(sys.argv[0]))

    parser = argparse.ArgumentParser(description='Requires 2 arguments, path of "input.json" and "output.json".')
    parser.add_argument("paths", metavar="path", type=str, nargs=2, help='path of "input.json" or "output.json"')
    args = parser.parse_args()
    input_json_path = args.paths[0]
    output_json_path = args.paths[1]
    with open(input_json_path, "r", encoding="utf-8") as f_in:
        config = json.load(f_in)

    specs_path = config["specsPath"]
    sdk_path = config["sdkPath"]
    sdk_examples_path = config["sdkExamplesPath"]
    tmp_path = config["tempPath"]

    release = Release(config["release"]["tag"], config["release"]["package"], config["release"]["version"])

    # find paths that contain the dotnet examples
    module_relative_path_local = get_module_relative_path(release.package, sdk_path)
    dotnet_examples_paths = find_dotnet_examples_paths(sdk_path, module_relative_path_local)

    sdk_package_path = os.path.join(sdk_path, module_relative_path_local)

    dotnet_module = f"{release.package},{release.version}"

    succeeded, files = create_dotnet_examples(release, dotnet_module, sdk_examples_path, dotnet_examples_paths)

    with open(output_json_path, "w", encoding="utf-8") as f_out:
        output = {"status": "succeeded" if succeeded else "failed", "name": dotnet_module, "files": files}
        json.dump(output, f_out, indent=2)


if __name__ == "__main__":
    main()
