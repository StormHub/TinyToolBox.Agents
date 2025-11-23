"use client";

import { CopilotChat } from "@copilotkit/react-ui";

export default function Page() {
  return (
    <main className="flex justify-center items-center h-full w-full">
      <div className="w-8/10 h-8/10 rounded-lg">
       <CopilotChat
          className="h-full rounded-2xl"
          labels={{ initial: "Hi, I'm an agent. Want to chat?" }}
        />
      </div>
    </main>
  );
}

