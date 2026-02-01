#!/bin/bash

# 1. Define the path to your test executable
# Adjust this path to match your actual build output directory
TEST_EXE="./bin/Debug/net8.0/MyTestProject.exe"

echo "Scanning for available tests..."

# 2. Extract tests from --list-tests
# We skip the first 3 lines to remove the Microsoft banner/header
mapfile -t TEST_LIST < <($TEST_EXE --list-tests | tail -n +4)

if [ ${#TEST_LIST[@]} -eq 0 ]; then
    echo "No tests found. Make sure the project is built."
    exit 1
fi

# 3. Create the Interactive Menu
PS3="Select a test to run (or type 'q' to quit): "

select TEST_NAME in "${TEST_LIST[@]}"; do
    if [[ "$REPLY" == "q" ]]; then
        echo "Exiting."
        break
    elif [[ -n "$TEST_NAME" ]]; then
        echo "---------------------------------------"
        echo "Running: $TEST_NAME"
        echo "---------------------------------------"
        
        # 4. Run the selected test using the --filter
        $TEST_EXE --filter "FullyQualifiedName=$TEST_NAME"
        break
    else
        echo "Invalid selection. Please choose a number from the list."
    fi
done
